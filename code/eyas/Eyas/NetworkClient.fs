﻿namespace Eyas.Network

open System
open System.Collections.Generic
open System.Net
open System.Net.Sockets
open System.Threading

type Client(port : int, randomSeed : int) =
    // helper function to choose the first element of a triple
    let first (one, two, three) = one

    member this.Run(servers : (string * int) [], minJobSize, maxJobSize, monitoringPeriod : int, msgsToSend : int, msgsPerSec : int) =
        let mutable queueLengths = servers |> Array.map(fun (hostname, port) -> (0, hostname, port))    // assume all servers have empty queues when we start

        let featurize = fun (qlens) ->
            qlens |> Array.map(fun (q,h,p) -> q)

        let computePCutOffs = fun (queues) ->
            let qlens = queues |> Array.sort
            let weights = qlens |> Array.map(fun (q,h,p) -> (q+1, q, h, p))
            let sumWeights = weights |> Array.sumBy(fun (w,q,h,p) -> w)
            weights
            |> Array.mapFold(fun lastSum (w, q, h, p) ->
                let cutOff = (lastSum + sumWeights - w)
                let probabilityOfSelection = (float (sumWeights - w)) / (float sumWeights * 2.0)
                ((cutOff, probabilityOfSelection, q, h, p), cutOff)) 0

        let mutable (pCutOffs, pCeil) = computePCutOffs(queueLengths)

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
                let (p, c) = computePCutOffs(queueLengths)
                pCutOffs <- p
                pCeil <- c
                Thread.Sleep(monitoringPeriod)
        }
        // Used to cancel the server queue monitoring thread
        use cancellationSource = new CancellationTokenSource()
        Async.Start(serverMonitor, cancellationSource.Token)

        let rand = new Random(randomSeed)
        let timer = new Diagnostics.Stopwatch()

        let jobsInFlight = new Dictionary<_, _>()

        let sender = async {
            let timeBetweenMsgs = new TimeSpan(int64(1000 * 1000 * 10 / msgsPerSec))  // a tick is 100 nanoseconds --> 1 sec = 10^7 ticks.
            use socket = new UdpClient()
            for i in 1..msgsToSend do
                //if i % 100 = 0 then printfn "--Sent %d messages." i
                // Pick the server to which the request will be forwarded
                let (_, p, qlen, serverHostname, serverPort) =
                    let r = rand.Next(pCeil)
                    Array.find(fun (cutOff, _, _, _, _) -> r < cutOff) pCutOffs

                // Send the message to the server and measure the extra delay
                let jobSize = rand.Next(minJobSize, maxJobSize)
                let sendTime = timer.ElapsedMilliseconds
                let msg = Array.concat [| BitConverter.GetBytes(jobSize); BitConverter.GetBytes(i); BitConverter.GetBytes(port) |]
                socket.Send(msg, msg.Length, serverHostname, serverPort) |> ignore

                let idx = queueLengths |> Array.findIndex (fun (q,h,p) -> p = serverPort)
                jobsInFlight.[i] <- (featurize(queueLengths), jobSize, idx + 1, p, sendTime)
                Thread.Sleep(timeBetweenMsgs)
            return null
        }

        let receiver = async {
            use socket = new UdpClient(port)
            let results = new List<_>()
            // This IPEndPoint object will allow us to read incoming datagrams sent from any source
            let anySender = new IPEndPoint(IPAddress.Any, 0)
            while results.Count < msgsToSend do
                //if results.Count % 100 = 0 then printfn "Received %d messages." results.Count
                let bytes = socket.Receive(ref anySender)
                let endTime = timer.ElapsedMilliseconds
                let jobSize = BitConverter.ToInt32(bytes, 0)
                let jobId = BitConverter.ToInt32(bytes, 4)
                
                let (qlens, jobSize, selectedServerIdx, p, sendTime) = jobsInFlight.[jobId]
                let delay = int(endTime - sendTime - int64(jobSize))
                results.Add((qlens, jobSize, selectedServerIdx, p, delay))
            return results      // return the delays experienced
        }

        timer.Start()
        let results = [receiver; sender]
                        |> Async.Parallel
                        |> Async.RunSynchronously
                        |> Array.head
        timer.Stop()
        cancellationSource.Cancel()     // stop the monitoring thread
        results          
