﻿using System;
using System.IO;
using System.Threading.Tasks;
using DiscUtils.Iso9660;
using ICSharpCode.SharpZipLib.Zip;
using RageCoop.Core;

namespace RageCoop.Server.Scripting;

internal class Resources
{
    private readonly Dictionary<string, Stream> ClientResources = new();
    private readonly Logger Logger;
    private readonly Dictionary<CDReader, Stream> Packages = new();
    private readonly Dictionary<string, Stream> ResourceStreams = new();
    private readonly Server Server;
    public Dictionary<string, ServerResource> LoadedResources = new();

    public Resources(Server server)
    {
        Server = server;
        Logger = server.Logger;
    }

    static Resources()
    {
        CoreUtils.ForceLoadAllAssemblies();
    }

    public bool IsLoaded { get; private set; }

    public void LoadAll()
    {
        // Packages
        {
            var path = Path.Combine("Resources", "Packages");
            Directory.CreateDirectory(path);
            foreach (var pkg in Directory.GetFiles(path, "*.respkg", SearchOption.AllDirectories))
                try
                {
                    Logger?.Debug($"Adding resources from package \"{Path.GetFileNameWithoutExtension(pkg)}\"");
                    var pkgStream = File.Open(pkg, FileMode.Open, FileAccess.Read, FileShare.Read);
                    var reader = new CDReader(pkgStream, true);
                    foreach (var e in reader.Root.GetDirectories("Client")[0].GetFiles())
                    {
                        var name = Path.GetFileNameWithoutExtension(e.Name);
                        if (!ClientResources.TryAdd(name, e.Open(FileMode.Open)))
                        {
                            Logger?.Warning($"Resource \"{name}\" already added, ignoring");
                            continue;
                        }

                        Logger?.Debug("Resource added: " + name);
                    }

                    foreach (var e in reader.Root.GetDirectories("Server")[0].GetFiles())
                    {
                        var name = Path.GetFileNameWithoutExtension(e.Name);
                        if (!ResourceStreams.TryAdd(name, e.Open(FileMode.Open)))
                        {
                            Logger?.Warning($"Resource \"{name}\" already loaded, ignoring");
                            continue;
                        }

                        Logger?.Debug("Resource added: " + name);
                    }

                    Packages.Add(reader, pkgStream);
                }
                catch (Exception ex)
                {
                    Logger?.Error("Failed to read resources from package: " + pkg, ex);
                }
        }


        // Client
        {
            var path = Path.Combine("Resources", "Client");
            var tmpDir = Path.Combine("Resources", "Temp", "Client");
            Directory.CreateDirectory(path);
            if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, true);
            Directory.CreateDirectory(tmpDir);
            var resourceFolders = Directory.GetDirectories(path, "*", SearchOption.TopDirectoryOnly);
            if (resourceFolders.Length != 0)
                foreach (var resourceFolder in resourceFolders)
                {
                    var zipPath = Path.Combine(tmpDir, Path.GetFileName(resourceFolder)) + ".res";
                    var name = Path.GetFileNameWithoutExtension(zipPath);
                    if (ClientResources.ContainsKey(name))
                    {
                        Logger?.Warning($"Client resource \"{name}\" already loaded, ignoring");
                        continue;
                    }

                    // Pack client side resource as a zip file
                    Logger?.Info("Packing client-side resource: " + resourceFolder);
                    try
                    {
                        using var zip = ZipFile.Create(zipPath);
                        zip.BeginUpdate();
                        foreach (var dir in Directory.GetDirectories(resourceFolder, "*", SearchOption.AllDirectories))
                            zip.AddDirectory(dir[(resourceFolder.Length + 1)..]);
                        foreach (var file in Directory.GetFiles(resourceFolder, "*", SearchOption.AllDirectories))
                        {
                            zip.Add(file, file[(resourceFolder.Length + 1)..]);
                        }

                        zip.CommitUpdate();
                        zip.Close();
                        ClientResources.Add(name, File.OpenRead(zipPath));
                    }
                    catch (Exception ex)
                    {
                        Logger?.Error($"Failed to pack client resource:{resourceFolder}");
                        Logger?.Error(ex);
                    }
                }
        }

        // Server
        {
            var path = Path.Combine("Resources", "Server");
            var dataFolder = Path.Combine(path, "data");
            Directory.CreateDirectory(path);
            foreach (var resource in Directory.GetDirectories(path))
                try
                {
                    var name = Path.GetFileName(resource);
                    if (LoadedResources.ContainsKey(name))
                    {
                        Logger?.Warning($"Resource \"{name}\" has already been loaded, ignoring...");
                        continue;
                    }

                    if (name.ToLower() == "data") continue;
                    Logger?.Info($"Loading resource: {name}");
                    var r = ServerResource.LoadFrom(resource, dataFolder, Logger);
                    LoadedResources.Add(r.Name, r);
                }
                catch (Exception ex)
                {
                    Logger?.Error($"Failed to load resource: {Path.GetFileName(resource)}");
                    Logger?.Error(ex);
                }

            foreach (var res in ResourceStreams)
                try
                {
                    var name = res.Key;
                    if (LoadedResources.ContainsKey(name))
                    {
                        Logger?.Warning($"Resource \"{name}\" has already been loaded, ignoring...");
                        continue;
                    }

                    Logger?.Info("Loading resource: " + name);
                    var r = ServerResource.LoadFrom(res.Value, name, Path.Combine("Resources", "Temp", "Server"),
                        dataFolder, Logger);
                    LoadedResources.Add(r.Name, r);
                }
                catch (Exception ex)
                {
                    Logger?.Error($"Failed to load resource: {res.Key}");
                    Logger?.Error(ex);
                }

            // Start scripts
            lock (LoadedResources)
            {
                foreach (var r in LoadedResources.Values)
                foreach (var s in r.Scripts.Values)
                {
                    s.API = Server.API;
                    try
                    {
                        Logger?.Debug("Starting script:" + s.CurrentFile.Name);
                        s.OnStart();
                    }
                    catch (Exception ex)
                    {
                        Logger?.Error($"Failed to start resource: {r.Name}");
                        Logger?.Error(ex);
                    }
                }
            }

            IsLoaded = true;
        }
    }

    public void UnloadAll()
    {
        lock (LoadedResources)
        {
            foreach (var d in LoadedResources.Values)
            {
                foreach (var s in d.Scripts.Values)
                    try
                    {
                        s.OnStop();
                    }
                    catch (Exception ex)
                    {
                        Logger?.Error(ex);
                    }

                try
                {
                    d.Dispose();
                }
                catch (Exception ex)
                {
                    Logger.Error($"Resource \"{d.Name}\" cannot be unloaded.");
                    Logger.Error(ex);
                }
            }

            LoadedResources.Clear();
        }

        foreach (var s in ClientResources.Values)
            try
            {
                s.Close();
                s.Dispose();
            }
            catch (Exception ex)
            {
                Logger?.Error("[Resources.CloseStream]", ex);
            }

        foreach (var s in ResourceStreams.Values)
            try
            {
                s.Close();
                s.Dispose();
            }
            catch (Exception ex)
            {
                Logger?.Error("[Resources.CloseStream]", ex);
            }

        foreach (var r in Packages)
        {
            r.Key.Dispose();
            r.Value.Dispose();
        }
    }

    public void SendTo(Client client)
    {
        Task.Run(() =>
        {
            try
            {
                if (ClientResources.Count != 0)
                {
                    Logger?.Info($"Sending resources to client:{client.Username}");
                    foreach (var rs in ClientResources)
                    {
                        Logger?.Debug(rs.Key);
                        Server.SendFile(rs.Value, rs.Key + ".res", client);
                    }

                    Logger?.Info($"Resources sent to:{client.Username}");
                }

                if (Server.GetResponse<Packets.FileTransferResponse>(client, new Packets.AllResourcesSent(),
                        ConnectionChannel.File, 30000)?.Response == FileResponse.Loaded)
                {
                    client.IsReady = true;
                    Server.API.Events.InvokePlayerReady(client);
                }
                else
                {
                    Logger?.Warning($"Client {client.Username} failed to load resource.");
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to send resource to client: " + client.Username, ex);
                client.Kick("Resource error!");
            }
        });
    }
}