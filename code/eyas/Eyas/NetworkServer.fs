namespace Eyas.Network

module Server =
    open System
    open System.Net
    open System.Net.Sockets
    open System.Threading


    let FlushPendingMessages(localPort : int, serverHostname : string, serverPort : int) =
        use socket = new UdpClient(localPort)
        let flushMsg = Array.concat [| BitConverter.GetBytes(-1); BitConverter.GetBytes(0); BitConverter.GetBytes(localPort) |]
        socket.Send(flushMsg, flushMsg.Length, serverHostname, serverPort) |> ignore
        socket.Receive(ref (new IPEndPoint(IPAddress.Any, 0))) |> ignore

    let Start(port : int, randomSeed : int, variablePerformance : bool, vpMultiplier : int, timePeriod : int, frequency : int, order : int) =
        let mutable rand = new Random(randomSeed)
        let mutable multiplier = 100

        use mutable cts = new CancellationTokenSource()
        let variablePerformanceThread =
            async {
                let mutable counter = 0
                while true do
                    counter <- counter + 1
                    if (counter % frequency) = order then
                        counter <- order
                        multiplier <- vpMultiplier
                    else
                        multiplier <- 100
                    printfn "--Multiplier set to x%d" multiplier
                    Thread.Sleep(timePeriod)
            }

        let reset () =
            cts.Cancel()
            cts.Dispose()
            cts <- new CancellationTokenSource()
            rand <- new Random(randomSeed)
            multiplier <- 100
            if variablePerformance then
                Async.Start(variablePerformanceThread, cts.Token)

        reset()

        // The server loop that does the actual job execution
        let server = MailboxProcessor<int * byte[] * int>.Start(fun inbox ->
             async {
                use replySocket = new UdpClient()
                let rec flush () =  async {
                    let! opt = inbox.TryReceive(1000)    // 1 second timeout to receive any in-flight messages -- this is overkill for co-located clients/servers
                    match opt with
                    | None -> ()
                    | Some _ -> do! flush()
                }
                while true do
                    let! jobSize, buffer, port = inbox.Receive()
                    if multiplier < 0 then
                       printfn "Flushing..."
                       let flushResponsePort = -1 * multiplier
                       do! flush()
                       reset()
                       printfn "done!"
                       replySocket.Send(buffer, buffer.Length, "127.0.0.1", flushResponsePort) |> ignore
                    else
                       Thread.Sleep(jobSize * multiplier / 100)
                       replySocket.Send(buffer, buffer.Length, "127.0.0.1", port) |> ignore
             })

        // The server loop that listens to incomming requests from clients
        use rcvSocket = new UdpClient(port)
        while true do
            let result = rcvSocket.ReceiveAsync() |> Async.AwaitTask |> Async.RunSynchronously
            let jobSize = BitConverter.ToInt32(result.Buffer, 0)
            if jobSize = 0 then     // report queue length
                let sender = result.RemoteEndPoint
                let qlen = server.CurrentQueueLength
                let response = BitConverter.GetBytes(server.CurrentQueueLength)
                rcvSocket.Send(response, response.Length, sender) |> ignore
            else     // append message to queue
                let port = BitConverter.ToInt32(result.Buffer, 8)
                if jobSize = -1 then multiplier <- -1 * port    // special code to flush queue
                server.Post(jobSize, result.Buffer, port)        