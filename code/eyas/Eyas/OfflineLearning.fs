namespace Eyas.Learning

module Offline =
    open VW
    open VW.Labels
    open LearningTypes
    open System
    open System.IO

    let WriteExplorationData (data: (int[] * int * int * float * int) []) =
        let path = "exploration_data.txt"
        if File.Exists(path) then File.Delete(path)
    
        use writer = new StreamWriter(path, true)
        data |> Array.iter (fun (qlens, jobSize, selectedServerIndex, probabilityOfSelection, experiencedDelay) ->
            let features =
                let tmp = qlens |> Array.mapi (fun i qlen -> sprintf "server%d:%d.0" (i+1) qlen)
                String.Join(" ", tmp)

            writer.WriteLine(sprintf "%d:%d:%.3f | %s" selectedServerIndex experiencedDelay probabilityOfSelection features) )

        data    // return the same data set in case it will be used again in a pipe


    let Learn (data : (int[] * int * int * float * int) []) =
        use vwDynamic =
            let settings = new VowpalWabbitSettings("--cb_adf --rank_all --interactions sp --interactions rp", featureDiscovery = Nullable(VowpalWabbitFeatureDiscovery.Json) )
            new VowpalWabbitDynamic(settings)

        let examples =
            data
            |> Array.map (fun (qlens, jobSize, selectedServerIndex, probabilityOfSelection, experiencedDelay) ->
                    let features =
                        qlens |> Array.mapi (fun i l -> {
                                                        Server = {Id = sprintf "server%d" (i+1)}
                                                        PerformanceStats = {QueueLength = (float32 l) } 
                                                    })

                    let example = {
                        _multi = features
                        Request = {JobSize = (float32 jobSize)}
                        }

                    let label = new ContextualBanditLabel(uint32 (selectedServerIndex + 1), (float32 experiencedDelay), (float32 probabilityOfSelection))
                    (features, label, selectedServerIndex) )

        examples
        |> Array.map (fun (features, label, actionIndex) -> vwDynamic.Learn(features, VowpalWabbitPredictionType.Multilabel, label, Nullable(actionIndex)))

