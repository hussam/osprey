open ToyExample.Local
open ToyExample.Networked

open System
open System.Collections.Generic

open FSharp.Charting


type Config = {
    isLocal : bool
    isServer : bool
    numLocalClients : int
    numLocalServers : int
    serverPort : int
    minJobSize : int
    maxJobSize : int
    refreshPeriod : int
    maxRefreshPeriod : int
    incRefreshPeriod : int
    servers : (string * int) list
}


let defaultConfig = {
    isLocal = true
    isServer = true
    numLocalClients = 10
    numLocalServers = 3
    serverPort = 8000
    minJobSize = 10
    maxJobSize = 100
    refreshPeriod = 30
    maxRefreshPeriod = 30
    incRefreshPeriod = Int32.MaxValue
    servers = []
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
    | false, true -> Server.Start(config.serverPort)
    | false, false ->
        let results =
            [ config.refreshPeriod .. config.incRefreshPeriod .. config.maxRefreshPeriod ]
            |> List.map (fun r ->
                    printf "Starting run with refresh every %d ms ... " r
                    let timer = Diagnostics.Stopwatch()
                    timer.Start()
                    let results =
                        [| 1..config.numLocalClients |]
                        |> Array.map(fun i ->
                                        let c = new Client(i)
                                        c.RunAsync( List.toArray config.servers, config.minJobSize, config.maxJobSize, config.refreshPeriod ) )
                        |> Async.Parallel
                        |> Async.RunSynchronously
                        |> Array.fold (fun accIn clientResults -> Array.append accIn (clientResults.ToArray())) [||]
                        |> Array.map (fun i -> int(i))  // cast results to integers
                    timer.Stop()
                    printfn "done in %d ms" timer.ElapsedMilliseconds
                    (r, results))
        let chart = Chart.BoxPlotFromData (results, ShowAverage = true, WhiskerPercentile = 5)
        Windows.Forms.Application.Run(chart.ShowChart())
    0 // return an integer exit code


    


