﻿using LyricsServer.Server;

namespace LyricsServer
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            MusixServer server = new MusixServer();
            Task handlerTask = server.Start(new string[] { "http://localhost:7545/" });
            handlerTask.GetAwaiter().GetResult();
        }
    }
}