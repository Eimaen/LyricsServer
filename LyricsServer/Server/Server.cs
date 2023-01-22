using System;
using System.Collections.Specialized;
using System.Net;
using System.Text;
using MusixmatchClientLib;
using MusixmatchClientLib.API.Model.Exceptions;
using MusixmatchClientLib.API.Model.Types;
using MusixmatchClientLib.Auth;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace LyricsServer.Server
{
	public class MusixServer : IDisposable
	{
        private const string TokenFile = "token.txt";

        public MusixmatchClient? Client;
        public HttpListener? Listener;
        private Task? ConnectionHandler;

        public bool ShutdownFlag { get; set; } = false;

        public void Dispose()
        {
            Listener?.Close();
            ConnectionHandler?.Dispose();
        }

        private struct JsonResponse
        {
            [JsonProperty("status")]
            public int Status;

            [JsonProperty("response")]
            public object Response;
        }

        public async Task Respond(HttpListenerResponse resp, string? format, int status, string response)
        {
            string result, mime;
            object final;
            try
            {
                final = JsonConvert.DeserializeObject(response);
            }
            catch
            {
                final = response;
            }
            if (format == "json")
            {
                result = JsonConvert.SerializeObject(new JsonResponse { Status = status, Response = final ?? new object() });
                mime = "application/json";
            }
            else
            {
                result = response?.ToString() ?? string.Empty;
                mime = "text/plain";
            }
            byte[] data = Encoding.UTF8.GetBytes(result);
            resp.StatusCode = 200;
            resp.ContentType = mime;
            resp.ContentEncoding = Encoding.UTF8;
            resp.ContentLength64 = data.LongLength;
            await resp.OutputStream.WriteAsync(data, 0, data.Length);
            resp.Close();
        }

        public async Task HandleMain(HttpListenerRequest req, HttpListenerResponse resp, NameValueCollection query)
        {
            await Respond(resp, "plain", 200, "Hello, this is a simple HTTP wrapper for MusixmatchClientLib.\n\nTry these endpoints:\n/getSubtitles?query=Cepheid - Catch Wind&format=json\n/getSubtitles?query=Cepheid - Catch Wind&format=lrc\n/getLyrics?query=Cepheid - Catch Wind&format=plain\n/getLyrics?query=Cepheid - Catch Wind&format=json");
        }

        public async Task HandleGetLyrics(HttpListenerRequest req, HttpListenerResponse resp, NameValueCollection query)
        {
            string format = string.IsNullOrWhiteSpace(query["format"] ?? "") ? "plain" : query["format"];

            if (query["query"] == null || query["query"] == string.Empty)
            {
                await Respond(resp, format, 400, "expected query parameter \"query\"");
                return;
            }

            string trackQuery = query["query"];
            string response = string.Empty;

            try
            {
                Track track = (await Client.SongSearchAsync(trackQuery)).First();
                response = (await Client.GetTrackLyricsAsync(track.TrackId)).LyricsBody;
            }
            catch (Exception ex)
            {
                if (ex is MusixmatchRequestException)
                {
                    if (((MusixmatchRequestException)ex).StatusCode == StatusCode.AuthFailed)
                    {
                        string token;
                        File.WriteAllText(TokenFile, token = new MusixmatchToken().Token);
                        Client = new MusixmatchClient(token);
                        Track track = (await Client.SongSearchAsync(trackQuery)).First();
                        response = (await Client.GetTrackLyricsAsync(track.TrackId)).LyricsBody;
                    }
                    else if (((MusixmatchRequestException)ex).StatusCode == StatusCode.ResourceNotFound)
                    {
                        await Respond(resp, format, 404, "lyrics for this track couldn't be found");
                        return;
                    }
                    else
                    {
                        await Respond(resp, format, 500, "TODO: insert blush sad anime girl here");
                        return;
                    }
                }
                else
                {
                    await Respond(resp, format, 500, "TODO: insert blush sad anime girl here");
                    return;
                }
            }
            await Respond(resp, format, 200, response);
        }

        private static Dictionary<string, MusixmatchClient.SubtitleFormat> FormatMapping = new Dictionary<string, MusixmatchClient.SubtitleFormat>
        {
            ["lrc"] = MusixmatchClient.SubtitleFormat.Lrc,
            ["json"] = MusixmatchClient.SubtitleFormat.Musixmatch
        };

        public async Task HandleGetSubtitles(HttpListenerRequest req, HttpListenerResponse resp, NameValueCollection query)
        {
            string format = string.IsNullOrWhiteSpace(query["format"] ?? "") ? "lrc" : query["format"];

            if (format != "lrc" && format != "json")
            {
                await Respond(resp, format, 400, "expected parameter \"format\" to be \"json\" or \"lrc\"");
                return;
            }

            if (query["query"] == null || query["query"] == string.Empty)
            {
                await Respond(resp, format, 400, "expected query parameter \"query\"");
                return;
            }

            string trackQuery = query["query"];
            string response = string.Empty;

            try
            {
                Track track = (await Client.SongSearchAsync(trackQuery)).First();
                response = (await Client.GetTrackSubtitlesRawAsync(track.TrackId, FormatMapping[format])).SubtitleBody;
            }
            catch (Exception ex)
            {
                if (ex is MusixmatchRequestException)
                {
                    if (((MusixmatchRequestException)ex).StatusCode == StatusCode.AuthFailed)
                    {
                        string token;
                        File.WriteAllText(TokenFile, token = new MusixmatchToken().Token);
                        Client = new MusixmatchClient(token);
                        Track track = (await Client.SongSearchAsync(trackQuery)).First();
                        response = (await Client.GetTrackSubtitlesRawAsync(track.TrackId, FormatMapping[format])).SubtitleBody;
                    }
                    else if (((MusixmatchRequestException)ex).StatusCode == StatusCode.ResourceNotFound)
                    {
                        await Respond(resp, format, 404, "lyrics for this track couldn't be found");
                        return;
                    }
                    else
                    {
                        await Respond(resp, format, 500, "TODO: insert blush sad anime girl here");
                        return;
                    }
                }
                else
                {
                    await Respond(resp, format, 500, "TODO: insert blush sad anime girl here");
                    return;
                }
            }
            await Respond(resp, format, 200, response);
        }

        public async Task HandleIncomingConnections()
        {
            while (!ShutdownFlag && Listener != null && Client != null)
            {
                HttpListenerContext ctx = await Listener.GetContextAsync();

                HttpListenerRequest req = ctx.Request;
                HttpListenerResponse resp = ctx.Response;
                NameValueCollection query = req.QueryString;

                if (req.Url == null)
                {
                    resp.Close();
                    return;
                }

                if (req.Url.AbsolutePath == "/")
                    await HandleMain(req, resp, query);

                if (req.Url.AbsolutePath == "/getLyrics")
                    await HandleGetLyrics(req, resp, query);

                if (req.Url.AbsolutePath == "/getSubtitles")
                    await HandleGetSubtitles(req, resp, query);
            }
        }

        public Task Start(string prefix)
        {
            string token;
            if (File.Exists(TokenFile))
                token = File.ReadAllText(TokenFile);
            else
                File.WriteAllText(TokenFile, token = new MusixmatchToken().Token);
            Client = new MusixmatchClient(token);
            Listener = new HttpListener();
            Listener.Prefixes.Add(prefix);
            Listener.Start();
            return ConnectionHandler = HandleIncomingConnections();
        }
    }
}

