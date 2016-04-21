namespace Eyas

module ArgsParser =
    open System
    open Configuration

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
            match (range.Split('-') |> Array.map Int32.Parse) with
            | [| size |] ->
                parseArgs ({config with minJobSize = size; maxJobSize = size}, tail)
            | [| min ; max |] ->
                parseArgs ({config with minJobSize = min; maxJobSize = max}, tail)
            | _ -> 
                parseArgs (config, tail)
        | "--refresh" :: range :: tail | "-r" :: range :: tail ->
            match range.Split(':') with
            | [| period |] -> 
                let p = Int32.Parse(period)
                parseArgs ({config with refreshPeriod = p; maxRefreshPeriod = p}, tail)
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
        | "--strategy" :: s :: tail | "-t" :: s :: tail ->
            let c =
                match s with
                | "random" -> {config with strategy = RandomSpray}
                | "weighted" -> {config with strategy = WeightedRandom}
                | "shortestQ" -> {config with strategy = ShortestQueue}
                | "learn" -> {config with strategy = OnlineLearning}
                | _ -> config
            parseArgs (c, tail)
        | arg :: tail ->  // unrecognized argument
            printfn "UNKOWN ARGUMENT: %s" arg
            parseArgs (config, tail)
        | [] ->
            config

