using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.Sharding;
using Akka.Cluster.Tools.Singleton;
using Akka.Configuration;
using Akkatecture.Clustering.Configuration;
using Akkatecture.Clustering.Core;
using Domain.Model.Account;
using Domain.Model.Account.ValueObjects;
using Domain.Repositories.Revenue;
using Domain.Repositories.Revenue.Commands;
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
            var amountOfWorkers = 9;

            //Create several workers with each worker port will be 6001, 6002,...
            var actorSystems = new List<ActorSystem>();
            foreach (var worker in Enumerable.Range(1, amountOfWorkers))
            {
                //Create worker with port 600X
                var config = ConfigurationFactory.ParseString($"akka.remote.dot-netty.tcp.port = 600{worker}");
                config = config
                    .WithFallback(baseConfig)
                    .WithFallback(AkkatectureClusteringDefaultSettings.DefaultConfig());
                var clustername = config.GetString("akka.cluster.name");
                var shardProxyRoleName = config.GetString("akka.cluster.singleton-proxy.role");
                var actorSystem = ActorSystem.Create(clustername, config);
                actorSystems.Add(actorSystem);
                
                //Start the aggregate cluster, all requests being proxied to this cluster will be 
                //sent here to be processed
                StartAggregateCluster(actorSystem);
                StartSagaCluster(actorSystem, shardProxyRoleName);
                StartSubscriber(actorSystem, shardProxyRoleName);
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
        
        public static void StartSagaCluster(ActorSystem actorSystem, string roleName)
        {
            
            var aggregateManager = ClusterFactory<AccountManager, Account, AccountId>
                .StartAggregateClusterProxy(actorSystem, roleName);
            
            ClusterFactory<MoneyTransferSagaManager, MoneyTransferSaga, MoneyTransferSagaId, MoneyTransferSagaLocator>
                .StartClusteredAggregateSaga(actorSystem, () => new MoneyTransferSaga(aggregateManager),  roleName);
        }

        public static void StartSubscriber(ActorSystem actorSystem, string roleName)
        {
            
            actorSystem.ActorOf(ClusterSingletonManager.Props(
                    singletonProps: Props.Create(() => new RevenueRepository()),
                    terminationMessage: PoisonPill.Instance,
                    settings: ClusterSingletonManagerSettings.Create(actorSystem).WithRole(roleName).WithSingletonName("revenuerepo-singleton")),
                    name: "repository");
            
            var repositoryProxy = actorSystem.ActorOf(ClusterSingletonProxy.Props(
                    singletonManagerPath: $"/user/repository",
                    settings: ClusterSingletonProxySettings.Create(actorSystem).WithRole(roleName).WithSingletonName("revenuerepo-singleton")),
                name: "repository-proxy");
            
            repositoryProxy.Tell(new AddRevenueCommand(new Money(1.0m)));

            SingletonFactory<RevenueSubscriber>
                .StartSingletonSubscriber(
                    actorSystem,
                () => new RevenueSubscriber(repositoryProxy), roleName);
        }
    }
}