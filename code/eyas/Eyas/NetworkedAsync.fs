namespace ToyExample.Networked.Async

open System
open System.Collections.Generic
open System.Net
open System.Net.Sockets
open System.Threading

module Server =
    let Start(port : int, randomSeed : int, variablePerformance : bool, minMultiplierPct : int, maxMultiplierPct : int, varPeriodFloor : int, varPeriodCeiling : int) =
        let rand = new Random(randomSeed)
        let mutable multiplier = 100

        match variablePerformance with
        | true ->
            async {
                while true do
                    let period = rand.Next(varPeriodFloor, varPeriodCeiling)
                    multiplier <- rand.Next(minMultiplierPct, maxMultiplierPct)
                    Thread.Sleep(period)
            } |> Async.Start
        | false -> ()   // do nothing

        // The server loop that does the actual job execution
        let server = MailboxProcessor<int * byte[] * int>.Start(fun inbox ->
            async {
                use replySocket = new UdpClient()
                while true do
                    let! jobSize, buffer, port = inbox.Receive()
                    Thread.Sleep(jobSize * multiplier / 100)
                    replySocket.Send(buffer, buffer.Length, "127.0.0.1", port) |> ignore
            } )

        // The server loop that listens to incomming requests from clients
        use rcvSocket = new UdpClient(port)
        while true do
            let result = rcvSocket.ReceiveAsync() |> Async.AwaitTask |> Async.RunSynchronously
            let jobSize = BitConverter.ToInt32(result.Buffer, 0)
            if jobSize = 0 then
                let sender = result.RemoteEndPoint
                let qlen = BitConverter.GetBytes(server.CurrentQueueLength)
                rcvSocket.Send(qlen, qlen.Length, sender) |> ignore
            else
                let port = BitConverter.ToInt32(result.Buffer, 12)
                server.Post(jobSize, result.Buffer, port)




type Client(id : int, port : int, randomSeed : int) =
    // helper function to choose the first element of a triple
    let first (one, two, three) = one

    member this.Run(servers : (string * int) [], minJobSize, maxJobSize, monitoringPeriod : int, numMessages : int) =
        let mutable currIndex = 0
        let mutable secondQlen = Int32.MaxValue
        let mutable queueLengths = servers |> Array.map(fun (hostname, port) -> (0, hostname, port))    // assume all servers have empty queues when we start

        // Periodically check the queue lengths at the different servers
        let serverMonitor = async {
            use socket = new UdpClient()
            let zero = BitConverter.GetBytes(0)
            let anySender = new IPEndPoint(IPAddress.Any, 0)    // needed by UdpClient.Receive()
            while true do
                queueLengths <- servers
                                |> Array.map (fun (hostname, port) -> 
                                        socket.Send(zero, zero.Length, hostname, port) |> ignore
                                        let result = socket.Receive(ref anySender)
                                        let qlen = BitConverter.ToInt32(result, 0)
                                        (qlen, hostname, port) )
                                |> Array.sortBy(fun (q, _, _) -> q)
                currIndex <- 0
                if servers.Length > 1 then
                    secondQlen <- first(queueLengths.[1])
                Thread.Sleep(monitoringPeriod)
        }
        // Used to cancel the server queue monitoring thread
        use cancellationSource = new CancellationTokenSource()
        Async.Start(serverMonitor, cancellationSource.Token)

        let rand = new Random(randomSeed)
        let timer = new Diagnostics.Stopwatch()
        timer.Start()

        let sender = async {
            use socket = new UdpClient()
            for i in 1..numMessages do
                // Pick the server to which the request will be forwarded
                if first(queueLengths.[currIndex]) > secondQlen then
                    let nextIndex = (currIndex + 1) % servers.Length
                    let nextnextIndex = (nextIndex + 1) % servers.Length
                    currIndex <- nextIndex
                    secondQlen <- first(queueLengths.[nextnextIndex])
                let (_, serverHostname, serverPort) = queueLengths.[currIndex]

                // Send the message to the server and measure the extra delay
                let jobSize = rand.Next(minJobSize, maxJobSize)
                let sendTime = timer.ElapsedMilliseconds
                let msg = Array.concat [| BitConverter.GetBytes(jobSize); BitConverter.GetBytes(sendTime); BitConverter.GetBytes(port) |]
                socket.Send(msg, msg.Length, serverHostname, serverPort) |> ignore
            return null
        }

        let receiver = async {
            use socket = new UdpClient(port)
            let results = new List<_>()
            // This IPEndPoint object will allow us to read incoming datagrams sent from any source
            let anySender = new IPEndPoint(IPAddress.Any, 0)
            while results.Count < numMessages do
                let bytes = socket.Receive(ref anySender)
                let jobSize = BitConverter.ToInt32(bytes, 0)
                let sendTime = BitConverter.ToInt64(bytes, 4)
                let endTime = timer.ElapsedMilliseconds
                results.Add(endTime - sendTime - int64(jobSize))    // record the delay
            return results      // return the delays experienced
        }

        let results = [receiver; sender]
                        |> Async.Parallel
                        |> Async.RunSynchronously
                        |> Array.head
        timer.Stop()
        cancellationSource.Cancel()     // stop the monitoring thread
        results                  