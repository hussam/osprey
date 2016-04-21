namespace Eyas.Learning

module LearningTypes =
    type Server =
        {
            Id : string
        }


    type PerformanceStats =
        {
            QueueLength : float32
        }


    type Request =
        {
            JobSize : float32
        }



    type FeatureBag =
        {
            //Server : Server
            PerformanceStats : PerformanceStats
        }


    type LearningExample =
        {
            _multi : FeatureBag[]
            //Request : Request
        }
