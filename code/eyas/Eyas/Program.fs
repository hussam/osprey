open Eyas
open Eyas.ArgsParser
open Eyas.Configuration
open Eyas.Network
open FSharp.Charting
open System
open System.Collections.Generic
                     

[<EntryPoint>]
let main args =
    let config = parseArgs (defaultConfig, Array.toList args)
    printfn "Configuration = %A\n" config
    match (config.isLocal, config.isServer) with
    | true, _ -> Local.Runner.Run(config.numLocalClients, config.numLocalServers, config.refreshPeriod, config.minJobSize, config.maxJobSize)
    | false, true ->
        Server.Start(config.serverPort, config.randomSeed, config.varPerf.isVariablePerf, config.varPerf.multiplier, config.varPerf.timePeriod, config.varPerf.frequency, config.varPerf.order)
    | false, false ->
        let results =
            [ config.refreshPeriod .. config.incRefreshPeriod .. config.maxRefreshPeriod ]
            |> List.map (fun r ->
                    printf "Flushing servers ... "
                    config.servers
                    |> List.mapi(fun i (host, port) -> async { Server.FlushPendingMessages(config.clientPortBase + i, host, port) })
                    |> Async.Parallel
                    |> Async.RunSynchronously
                    |> ignore
                    printfn "done"
                    printf "Starting run with refresh every %d ms ... " r
                    let timer = Diagnostics.Stopwatch()
                    timer.Start()
                    let results =
                        [| 1..config.numLocalClients |]
                        |> Array.map(fun i ->
                                        let c = new Client(config.clientPortBase + i, config.randomSeed)
                                        async { return c.Run( List.toArray config.servers, config.minJobSize, config.maxJobSize, config.refreshPeriod, config.msgsToSend, config.msgsPerSec ) } )
                        |> Async.Parallel
                        |> Async.RunSynchronously
                        |> Array.fold (fun accIn clientResults -> Array.append accIn (clientResults.ToArray())) [||]
                        |> Learning.Offline.WriteExplorationData
                        |> Array.map (fun ((_, _, _, _, experiencedDelay) as r) -> experiencedDelay)    // cast results to integers
                        |> Array.sort
                        |> Array.mapi (fun i latency -> (100.0 * float(i+1) / float(config.msgsToSend), latency))
                    timer.Stop()
                    printfn "done in %d ms" timer.ElapsedMilliseconds
                    (r, results))
        //results |> List.iter(fun (r, results) -> for r in results do printfn "%A" r)
        let chart =
            results
            |> List.map (fun (r, results) -> let title = (sprintf "Probe Queues Every %d ms" r) in Chart.Line(results, Name=title).WithLegend(Enabled=true))
            |> Chart.Combine
            |> (fun chart -> chart.WithYAxis(Title="Added Latency (ms)").WithXAxis(Title="Pct of Requests"))
        //chart |> Chart.Save(sprintf "results/chart-%s.png" (DateTime.Now.ToString "MM-dd-HH-mm"))
        printfn "Number of results = %d" results.Length

        results
        |> List.map (fun (r, results) -> (r, (results |> Array.map (fun (i, latency) -> float latency) |> Array.average)))
        |> List.iter (fun (r, avg) -> printfn "When probing queues every %d ms, average added delay was %.3f" r avg)

        Windows.Forms.Application.Run(chart.ShowChart())
    0 // return an integer exit code