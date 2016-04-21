namespace Eyas.Learning

module OnlineLearning =
    open LearningTypes
    open System
    open VW
    open VW.Labels
    open Microsoft.Research.MultiWorldTesting.ExploreLibrary

    let Init (seed : int) (epsilon : float) (numMessages : int) =
        let vw =
            let settings = new VowpalWabbitSettings("--cb_adf --rank_all", featureDiscovery = Nullable(VowpalWabbitFeatureDiscovery.Json) )
            new VowpalWabbit<LearningExample>(settings)
        let explorer = new TopSlotExplorer(new EpsilonGreedyExplorer(float32 epsilon))
        let probabilityGenerator = new PRG(uint64 seed)
        let predictions = Array.create numMessages ({_multi = [||]}, Int32.MinValue, float32 0)
        (vw, explorer, probabilityGenerator, predictions)


    let Predict (vw : VowpalWabbit<LearningExample>) (explorer : TopSlotExplorer) (prg : PRG) (predictions : (LearningExample * int * float32)[]) (msgNum : int) (serverQueues : Eyas.Strategies.Feature[]) =
        let features =
            serverQueues
            |> Array.map (fun (hostname, port, qlen) ->
                                {
                                    PerformanceStats = {QueueLength = (float32 qlen) } 
                                })
        let example = {
            _multi = features
            //Request = {JobSize = (float32 jobSize)}
            }

        // NOTE: this is not thread safe!
        let prediction = vw.Predict(example, VowpalWabbitPredictionType.Multilabel)
        let decision, probability =
            let rankings = explorer.MapContext(prg, prediction, prediction.Length)
            let probability = (rankings.ExplorerState :?> GenericExplorerState).Probability
            (rankings.Value.[0], probability)
        // Save in-flight prediction
        predictions.[msgNum] <- (example, 0, probability)
        (decision, (float probability))


    let Learn (vw : VowpalWabbit<LearningExample>) (predictions : (LearningExample * int * float32)[]) (msgNum : int) (experiencedDelay : int) =
        let reward = float32(-1 * experiencedDelay)
        let (example, playedAction, probability) = predictions.[msgNum]
        let label = new ContextualBanditLabel(uint32 playedAction, -reward, probability)
        let index = new Nullable<int>(playedAction - 1)
        vw.Learn(example, label, index)