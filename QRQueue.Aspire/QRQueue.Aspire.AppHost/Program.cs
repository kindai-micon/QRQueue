using Projects;

var builder = DistributedApplication.CreateBuilder(args);
var db = builder.AddPostgres("LotteryDb")
    .AddDatabase("lottery-db");
var main = builder.AddProject<QRQueue>("main")
    .WithReference(db)
    .WaitFor(db);

builder.Build().Run();
