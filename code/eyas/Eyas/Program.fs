open ToyExample.Local
open ToyExample.Networked

open System
open System.Collections.Generic

open FSharp.Charting


type VariablePerformanceConfig = {
    isVariablePerf : bool
    multiplier : int
    timePeriod : int
    frequency : int
    order : int
}

type Config = {
    isLocal : bool
    isServer : bool
    isAsync : bool
    numLocalClients : int
    numLocalServers : int
    serverPort : int
    minJobSize : int
    maxJobSize : int
    refreshPeriod : int
    maxRefreshPeriod : int
    incRefreshPeriod : int
    servers : (string * int) list
    msgsToSend : int
    msgsPerSec : int
    clientPortBase : int
    randomSeed : int
    varPerf : VariablePerformanceConfig
}


let defaultConfig = {
    isLocal = true
    isServer = true
    isAsync = true
    numLocalClients = 10
    numLocalServers = 3
    serverPort = 8000
    minJobSize = 10
    maxJobSize = 100
    refreshPeriod = 30
    maxRefreshPeriod = 30
    incRefreshPeriod = Int32.MaxValue
    servers = []
    msgsToSend = 1
    msgsPerSec = 100
    clientPortBase = 4000
    randomSeed = 3000
    varPerf = {isVariablePerf = false; multiplier = 100; timePeriod = Int32.MaxValue; frequency = Int32.MaxValue; order = 0}
}




let runLocal config =
    Runner.startServers(config.numLocalServers, config.refreshPeriod)
    Runner.startClients(config.numLocalClients, Runner.startLoadBalancer(), config.minJobSize, config.maxJobSize)
    System.Threading.Thread.Sleep(60000)



let rec parseArgs (config, args : string list) =
    // XXX: no input validation whatsoever. This is bad if it were real code
    match args with
    | "--NumLocalClients" :: lc :: tail | "-lc" :: lc :: tail ->
        let numClients = Int32.Parse(lc)
        parseArgs ({config with numLocalClients = numClients}, tail)
    | "--NumLocalServers" :: ls :: tail | "-ls" :: ls :: tail ->
        let numServers = Int32.Parse(ls)
        parseArgs ({config with numLocalServers = numServers}, tail)
    | "--networked" :: tail | "-n" :: tail ->
        parseArgs ({config with isLocal = false}, tail)
    | "--host" :: port :: tail | "-h" :: port :: tail ->
        let p = Int32.Parse(port)
        parseArgs ({config with serverPort = p}, tail)
    | "--client" :: nc :: tail | "-c" :: nc :: tail ->
        let numClients = Int32.Parse(nc)
        parseArgs ({config with isServer = false; numLocalClients = numClients}, tail)
    | "--jobs" :: range :: tail | "-j" :: range :: tail ->
        let sizes = range.Split('-') |> Array.map(Int32.Parse)
        parseArgs ({config with minJobSize = sizes.[0]; maxJobSize = sizes.[1]}, tail)
    | "--refresh" :: period :: tail | "-r" :: period :: tail ->
        let p = Int32.Parse(period)
        parseArgs ({config with refreshPeriod = p; maxRefreshPeriod = p}, tail)
    | "--refreshRange" :: range :: tail | "-rr" :: range :: tail ->
        match range.Split(':') with
        | [| start ; step ; _end |] ->
            let s = Int32.Parse(start)
            let e = Int32.Parse(_end)
            let t = Int32.Parse(step)
            parseArgs ({config with refreshPeriod = s; maxRefreshPeriod = e; incRefreshPeriod = t}, tail)
        | _ ->
            parseArgs (config, tail)
    | "--server" :: server :: tail | "-s" :: server :: tail ->
        let addr = server.Split(':')
        let host = addr.[0]
        let port = Int32.Parse(addr.[1])
        parseArgs ({config with servers = (host, port) :: config.servers}, tail)
    | "--async" :: tail | "-a" :: tail ->
        parseArgs ({config with isAsync = true}, tail)
    | "--msgsToSend" :: nm :: tail | "-m" :: nm :: tail ->
        let n = Int32.Parse(nm)
        parseArgs ({config with msgsToSend = n}, tail)
    | "--msgsPerSec" :: mps :: tail | "-mps" :: mps :: tail ->
        let r = Int32.Parse(mps)
        parseArgs ({config with msgsPerSec = r}, tail)
    | "--randomSeed" :: rs :: tail | "-rs" :: rs :: tail ->
        let r = Int32.Parse(rs)
        parseArgs ({config with randomSeed = r}, tail)
    | "--variablePerformance" :: vp :: tail | "-vp" :: vp :: tail ->
        match vp.Split(':') with
        | [| multiplier |] ->
            let v = {
                isVariablePerf = true;
                multiplier = Int32.Parse(multiplier)
                timePeriod = Int32.MaxValue
                frequency  = 1
                order = 0
            }
            parseArgs ({config with varPerf = v}, tail)
        | [| multiplier ; period ; frequency ; order |] ->
            let v = {
                isVariablePerf = true
                multiplier = Int32.Parse(multiplier)
                timePeriod = Int32.Parse(period)
                frequency  = Int32.Parse(frequency)
                order      = Int32.Parse(order) - 1
            }
            parseArgs ({config with varPerf = v}, tail)
        | _ ->
            parseArgs (config, tail)
    | arg :: tail ->  // unrecognized argument
        printfn "UNKOWN ARGUMENT: %s" arg
        parseArgs (config, tail)
    | [] ->
        config
                        

[<EntryPoint>]
let main args =
    let config = parseArgs (defaultConfig, Array.toList args)
    printfn "Configuration = %A\n" config
    match (config.isLocal, config.isServer) with
    | true, _ -> runLocal config
    | false, true ->
        Async.Server.Start(config.serverPort, config.randomSeed, config.varPerf.isVariablePerf, config.varPerf.multiplier, config.varPerf.timePeriod, config.varPerf.frequency, config.varPerf.order)
    | false, false ->
        let results =
            [ config.refreshPeriod .. config.incRefreshPeriod .. config.maxRefreshPeriod ]
            |> List.map (fun r ->
                    if config.isAsync then
                        printf "Flushing servers ... "
                        config.servers
                        |> List.mapi(fun i (host, port) -> async { Async.Server.FlushPendingMessages(config.clientPortBase + i, host, port) })
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
                                        let c = new Async.Client(config.clientPortBase + i, config.randomSeed)
                                        async { return c.Run( List.toArray config.servers, config.minJobSize, config.maxJobSize, config.refreshPeriod, config.msgsToSend, config.msgsPerSec ) } )
                        |> Async.Parallel
                        |> Async.RunSynchronously
                        |> Array.fold (fun accIn clientResults -> Array.append accIn (clientResults.ToArray())) [||]
                        |> Array.map int    // cast results to integers
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
        Windows.Forms.Application.Run(chart.ShowChart())
    0 // return an integer exit code