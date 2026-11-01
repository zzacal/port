if (args.Length > 0 && args[0] == "remove-seeded")
{
    return await Gallery.Web.Maintenance.RemoveSeededCommand.RunAsync(args, Console.Out);
}

if (args.Length > 0 && args[0] == "seed")
{
    Console.Out.WriteLine("Unknown command: seed");
    return 64;
}

Gallery.Web.AppBuilder.Build(args).Run();
return 0;
