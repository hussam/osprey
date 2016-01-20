namespace ToyExample.Local

type Agent = MailboxProcessor<int *AsyncReplyChannel<unit>>

module Server =
    open System.Threading

    // Starts a server
    let private server() =
        Agent.Start(fun inbox ->
            async {
                let rand = System.Random()
                while true do
                    let! msg, replyChannel = inbox.Receive()
                    //if (rand.Next(15) = 0) then Thread.Sleep(rand.Next(100, 1000))  // interference
                    Thread.Sleep(msg)
                    replyChannel.Reply()
            } )

    // Starts a perf reporter for the given server 's'
    let private reporter(server : MailboxProcessor<_>) =
        MailboxProcessor.Start(fun inbox ->
            async {
                while true do
                    let! (replyChannel : AsyncReplyChannel<int>) = inbox.Receive()
                    replyChannel.Reply(server.CurrentQueueLength)
            } )

    let Start() =
        let s = server()
        (s, reporter(s))



module Runner =
    open System
    open System.Threading

    let mutable servers = [| |]
    let mutable currIndex = 0
    let mutable secondQlen = Int32.MaxValue

    let startServers(num, monitoringPeriod : int) =
        servers <- (Array.init num (fun _ -> (0, Server.Start())))
        let monitor() =
            async {
                while true do
                    servers |> Array.iter(fun (q,_) -> Console.Write("{0}\t", q))
                    Console.WriteLine "----"
                    servers <- servers
                                |> Array.map(fun (_, (s, r)) -> (r.PostAndReply(fun rc -> rc), (s, r)))
                                |> Array.sortBy(fun (q, _) -> q)
                    currIndex <- 0
                    if num > 1 then secondQlen <- fst servers.[1]
                    Thread.Sleep(monitoringPeriod)
                    
            }
        monitor() |> Async.Start

    let startLoadBalancer() =
        Agent.Start(fun inbox ->
            async {
                while true do
                    // Receive a message from a client
                    let! msg, clientReplyChannel = inbox.Receive()
                    // Pick which server to forward the request to
                    if fst servers.[currIndex] > secondQlen then
                        let nextIndex = (currIndex + 1) % servers.Length
                        let nextnextIndex = (nextIndex + 1) % servers.Length
                        currIndex <- nextIndex
                        secondQlen <- fst servers.[nextnextIndex]
                    // Forward the message to the server and tell it to then forward the response back to the client
                    let _, (s, _) = servers.[currIndex]
                    s.Post((msg, clientReplyChannel))
            })

    let startClients(num, loadBalancer : Agent, minJobSize, maxJobSize) =
        for i = 1 to num do
            async {
                let rand = new Random()
                let timer = new Diagnostics.Stopwatch()
                timer.Start()
                while true do
                    let sendTime = timer.ElapsedMilliseconds
                    loadBalancer.PostAndReply(fun rc -> (rand.Next(minJobSize, maxJobSize), rc))
                    //let endTime = timer.ElapsedMilliseconds
                    //printfn "%d" (endTime - sendTime)
            } |> Async.Start