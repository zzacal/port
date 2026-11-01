if (args.Length > 0 && args[0] == "seed")
{
    return await Gallery.Web.Seed.SeedCommand.RunAsync(args, Console.Out);
}

Gallery.Web.AppBuilder.Build(args).Run();
return 0;
