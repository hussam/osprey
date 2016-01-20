namespace ToyExample.Networked

open System
open System.Collections.Generic
open System.Net
open System.Net.Sockets
open System.Threading

type Agent = MailboxProcessor<int * IPEndPoint>

module Server =
    let Start(port : int) =
        // The server loop that does the actual job execution
        let server = Agent.Start(fun inbox ->
            async {
                use replySocket = new UdpClient()
                let zeros = BitConverter.GetBytes(0)
                let zlen = zeros.Length
                while true do
                    let! msg, replyEndpoint = inbox.Receive()
                    Thread.Sleep(msg)
                    replySocket.Send(zeros, zlen, replyEndpoint) |> ignore
            } )

        // The server loop that listens to incomming requests from clients
        use rcvSocket = new UdpClient(port)
        while true do
            let result = rcvSocket.ReceiveAsync() |> Async.AwaitTask |> Async.RunSynchronously
            let jobSize = BitConverter.ToInt32(result.Buffer, 0)
            let sender = result.RemoteEndPoint
            if jobSize = 0 then
                let qlen = BitConverter.GetBytes(server.CurrentQueueLength)
                rcvSocket.Send(qlen, qlen.Length, sender) |> ignore
            else
                server.Post(jobSize, sender)




type Client(id : int) =
    // helper function to choose the first element of a triple
    let first (one, two, three) = one

    member this.RunAsync(servers : (string * int) [], minJobSize, maxJobSize, monitoringPeriod : int) = async {
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


        let rand = new Random(3000)
        let timer = new Diagnostics.Stopwatch()
        let results = new List<_>()
        // This IPEndPoint object will allow us to read incoming datagrams sent from any source
        let anySender = new IPEndPoint(IPAddress.Any, 0)

        use socket = new UdpClient()

        timer.Start()
        for i in 1..100 do
            let jobSize = rand.Next(minJobSize, maxJobSize)
            let msg = BitConverter.GetBytes(jobSize)

            // Pick the server to which the request will be forwarded
            if first(queueLengths.[currIndex]) > secondQlen then
                let nextIndex = (currIndex + 1) % servers.Length
                let nextnextIndex = (nextIndex + 1) % servers.Length
                currIndex <- nextIndex
                secondQlen <- first(queueLengths.[nextnextIndex])
            let (_, serverHostname, serverPort) = queueLengths.[currIndex]

            // Send the message to the server and measure the extra delay
            let sendTime = timer.ElapsedMilliseconds
            socket.Send(msg, msg.Length, serverHostname, serverPort) |> ignore
            socket.Receive(ref anySender) |> ignore
            let endTime = timer.ElapsedMilliseconds
            results.Add(endTime - sendTime - int64(jobSize))    // record the delay
        timer.Stop()
        cancellationSource.Cancel()     // stop the monitoring thread
        return results                  // return the delays experienced
    }