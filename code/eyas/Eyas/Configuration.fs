namespace Eyas

module Configuration =
    open System

    /// Load balancer's strategy for routing jobs
    type Strategy =
        | Random
        | QueueWeighted


    /// Configuration for varying server performance (aka the "devil")
    type VariablePerformanceConfig = {
        isVariablePerf : bool
        multiplier : int
        timePeriod : int
        frequency : int
        order : int
    }


    /// General configuration of the program.
    /// Including things like: Is this a client or a server? How many jobs to send? How frequently? How to balancer requests? ...etc
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
        msgsToSend : int
        msgsPerSec : int
        clientPortBase : int
        randomSeed : int
        varPerf : VariablePerformanceConfig
        strategy : Strategy
    }


    /// Default configuration for an Eyas instance
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
        msgsToSend = 1
        msgsPerSec = 100
        clientPortBase = 4000
        randomSeed = 3000
        varPerf = {isVariablePerf = false; multiplier = 100; timePeriod = Int32.MaxValue; frequency = Int32.MaxValue; order = 0}
        strategy = Random
    }