if (args.Length > 0 && args[0] == "seed")
{
    // T032 wires the real seed command; stub returns 1 until then.
    return 1;
}

Gallery.Web.AppBuilder.Build(args).Run();
return 0;
