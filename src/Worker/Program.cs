using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akkatecture.Clustering.Configuration;
using Akkatecture.Clustering.Core;
using Domain.Model.Account;
using Domain.Repositories.Revenue;
using Domain.Sagas.MoneyTransfer;
using Domain.Subscribers.Revenue;

namespace Worker
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            //Get configuration file using Akkatecture's defaults as fallback
            var path = Environment.CurrentDirectory;
            var configPath = Path.Combine(path, "worker.conf");
            var baseConfig = ConfigurationFactory.ParseString(File.ReadAllText(configPath));
            
            //specified amount of workers running on their own thread
            var amountOfWorkers = 10;

            //Create several workers with each worker port will be 6001, 6002,...
            var actorSystems = new List<ActorSystem>();
            foreach (var worker in Enumerable.Range(1, amountOfWorkers+1))
            //foreach (var worker in Enumerable.Range(1, 1))
            {
                //Create worker with port 600X
                var config = ConfigurationFactory.ParseString($"akka.remote.dot-netty.tcp.port = 600{worker}");
                config = config
                    .WithFallback(baseConfig)
                    .WithFallback(AkkatectureClusteringDefaultSettings.DefaultConfig());
                var clustername = config.GetString("akka.cluster.name");
                var actorSystem = ActorSystem.Create(clustername, config);
                actorSystems.Add(actorSystem);
                
                //Start the aggregate cluster, all requests being proxied to this cluster will be 
                //sent here to be processed
                StartAggregateCluster(actorSystem);
                StartAggregateCluster(actorSystem);
            }

            Console.WriteLine("Worker Running");

            var quit = false;
            
            while (!quit)
            {
                Console.Write("\rPress Q to Quit.         ");
                var key = Console.ReadLine();
                quit = key?.ToUpper() == "Q";
            }

            //Shut down all the local actor systems
            foreach (var actorsystem in actorSystems)
            {
                await actorsystem.Terminate();
            }
            Console.WriteLine("Worker Exiting.");
        }
        
        public static void StartAggregateCluster(ActorSystem actorSystem)
        {
            ClusterFactory<AccountManager, Account, AccountId>
                .StartClusteredAggregate(actorSystem);
        }
        
        public static void StartSagaCluster(ActorSystem actorSystem)
        {
            
            var aggregateManager = ClusterFactory<AccountManager, Account, AccountId>
                .StartAggregateClusterProxy(actorSystem, "worker", 12);
            
            ClusterFactory<MoneyTransferSagaManager, MoneyTransferSaga, MoneyTransferSagaId, MoneyTransferSagaLocator>
                .StartClusteredAggregateSaga(actorSystem, () => new MoneyTransferSaga(aggregateManager),  "worker", 12);
        }

        public static void StartSubscriber(ActorSystem actorSystem)
        {
            //Create revenue repository
            var revenueRepository = actorSystem.ActorOf(Props.Create(() => new RevenueRepository()), "revenue-repository");

            //Create subscriber for revenue repository
            actorSystem.ActorOf(Props.Create(() => new RevenueSubscriber(revenueRepository)), "revenue-subscriber");
        }
    }
}