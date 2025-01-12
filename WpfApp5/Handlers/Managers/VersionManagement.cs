﻿using Newtonsoft.Json.Linq;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Threading.Tasks;
using Windows.Management.Deployment;
using Application = System.Windows.Application;

namespace Flarial.Launcher.Managers
{
    public class VersionManagement
    {
        public static string launcherPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Flarial", "Launcher");


        public static string ExtractUrl(string jsonString)
        {
            // Parse the JSON string into a JObject
            JObject jsonObject = JObject.Parse(jsonString);

            // Get the value of the "url" property
            string url = (string)jsonObject["url"];

            return url;
        }

        public static async Task<string> GetVersionLinkAsync(string version)
        {
            string result = "";
            WebClient webClient = new WebClient();
            if (version == "1.20.0.1") result = ExtractUrl(webClient.DownloadStringTaskAsync(new Uri("https://api.jiayi.software/api/v1/minecraft/download_url?version=1.20.0.1&arch=x64")).Result);
            if (version == "1.20.10") result = ExtractUrl(webClient.DownloadStringTaskAsync(new Uri("https://api.jiayi.software/api/v1/minecraft/download_url?version=1.20.10.1&arch=x64")).Result);

            Trace.WriteLine(version);

            return result;
        }

        public static async Task<bool> DownloadApplication(string version)
        {
            string path = Path.Combine(launcherPath, "Versions", $"Minecraft{version}.Appx");
            string url = await GetVersionLinkAsync(version);

            if (File.Exists(path))
            {
                Trace.WriteLine("File already exists, download skipped.");
                return true;
            }

            if (!string.IsNullOrEmpty(url))
            {
                Trace.WriteLine($"Got downloadable URL for Minecraft version {version}: {url}");
                using (WebClient webClient = new WebClient())
                {
                    webClient.DownloadProgressChanged += DownloadProgressCallback;
                    await webClient.DownloadFileTaskAsync(new Uri(url), path);
                    Trace.WriteLine("Download succeeded!");
                }
            }
            else
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    CustomDialogBox MessageBox = new CustomDialogBox("Download failed",
                        $"{url} issue with the URL. Please report this in our discord.", "MessageBox");
                    MessageBox.ShowDialog();
                });

                return false;
            }

            return true;
        }
        public static async Task<bool> InstallAppBundle(string dir)
        {

            Trace.WriteLine("called installappbundle");
            Trace.WriteLine(dir);
            try
            {
                var packageUri = new Uri(Path.Combine(dir, "AppxManifest.xml"));
                Trace.WriteLine(packageUri.ToString());


                File.Delete(Path.Combine(dir, "AppxSignature.p7x"));
                var packageManager = new PackageManager();

                var addPackageOperation = packageManager.RegisterPackageAsync(packageUri, null, DeploymentOptions.ForceUpdateFromAnyVersion | DeploymentOptions.DevelopmentMode);
                addPackageOperation.Progress += (sender, args) => ReportProgress(args);

                var addPackageTask = addPackageOperation.AsTask();
                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(300)); // Adjust timeout duration as needed

                var completedTask = await Task.WhenAny(addPackageTask, timeoutTask);

                if (completedTask == addPackageTask && addPackageTask.Status == TaskStatus.RanToCompletion)
                {
                    // Check if the package is registered after deployment
                    Minecraft.Init();
                    var packageName = Minecraft.Package;

                    if (packageName != null)
                    {
                        Trace.WriteLine("Installation succeeded!");
                        return true;
                    }
                    else
                    {
                        Trace.WriteLine("Package registration check failed.");
                        return false;
                    }
                }
                else
                {
                    Trace.WriteLine("Installation timed out.");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"AddPackageSample failed, error message: {ex.Message}\nFull Stacktrace: {ex.ToString()}");
                return false;
            }
        }

        private static void ReportProgress(DeploymentProgress progress)
        {
            // Report the progress of the deployment
            if (progress.percentage != 100)
                MainWindow.progressPercentage = (int)progress.percentage;

            MainWindow.progressType = "Installing";
        }

        private static void DownloadProgressCallback(object sender, DownloadProgressChangedEventArgs e)
        {
            MainWindow.progressType = "download";

            if (e.ProgressPercentage != 100)
                MainWindow.progressPercentage = e.ProgressPercentage;

            MainWindow.progressBytesReceived = e.BytesReceived;
            MainWindow.progressBytesTotal = e.TotalBytesToReceive;
            Trace.WriteLine($"Downloaded {e.BytesReceived} of {e.TotalBytesToReceive} bytes. {e.ProgressPercentage}% complete...");
        }

        public static async Task<bool> RemoveMinecraftPackage()
        {
            try
            {
                var packageManager = new PackageManager();

                var progress = new Progress<DeploymentProgress>(ReportProgress);
                var removePackageOperation = packageManager.RemovePackageAsync(Minecraft.Package.Id.FullName, RemovalOptions.None);
                removePackageOperation.Progress += (sender, args) => ReportProgress(args);

                var removePackageTask = removePackageOperation.AsTask();

                await removePackageTask;

                if (removePackageTask.Status == TaskStatus.RanToCompletion)
                {

                    Trace.WriteLine("Package removal succeeded!");
                    return true;
                }
                else
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        CustomDialogBox MessageBox =
                            new CustomDialogBox("Failed", "Failed to remove package.", "MessageBox");
                        MessageBox.ShowDialog();
                    });
                    return false;
                }
            }
            catch (Exception ex)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    CustomDialogBox MessageBox = new CustomDialogBox("Failed",
                        $"RemovePackageAsync failed, error message: {ex.Message}\nFull Stacktrace: {ex.ToString()}",
                        "MessageBox");
                    MessageBox.ShowDialog();
                });
                return false;
            }
        }

        public static async Task ExtractAppxAsync(string appxFilePath, string outputFolderPath, string version)
        {
            int totalEntries = 0;
            int currentEntry = 0;

            if (!Directory.Exists(outputFolderPath))
            {
                using (ZipArchive archive = ZipFile.OpenRead(appxFilePath))
                {
                    totalEntries = archive.Entries.Count;

                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        string entryOutputPath = Path.Combine(outputFolderPath, entry.FullName);
                        string entryDirectory = Path.GetDirectoryName(entryOutputPath);

                        if (!string.IsNullOrEmpty(entryDirectory))
                        {
                            Directory.CreateDirectory(entryDirectory);
                        }

                        if (!entry.FullName.EndsWith("/"))
                        {
                            await Task.Run(() => entry.ExtractToFile(entryOutputPath, true));
                        }

                        currentEntry++;
                        MainWindow.progressType = "Extracting";
                        MainWindow.progressBytesTotal = totalEntries;
                        MainWindow.progressBytesReceived = currentEntry;
                        MainWindow.progressPercentage = (int)((float)currentEntry / totalEntries * 100);
                    }
                }

                WebClient webClient = new WebClient();

                if (File.Exists(Path.Combine(launcherPath, "Versions", version, "UAP.Assets", "minecraft", "icons", "MCSplashScreen.scale-200.png")))
                    File.Delete(Path.Combine(launcherPath, "Versions", version, "UAP.Assets", "minecraft", "icons", "MCSplashScreen.scale-200.png"));

                if (File.Exists(Path.Combine(launcherPath, "Versions", version, "data", "resource_packs", "vanilla", "textures",
                       "ui", "title.png")))
                    File.Delete(Path.Combine(launcherPath, "Versions", version, "data", "resource_packs", "vanilla", "textures",
                        "ui", "title.png"));

                using (StreamWriter writer = new StreamWriter(Path.Combine(launcherPath, "Versions", version, "data", "resource_packs", "vanilla", "splashes.json"), false))
                {
                    string jsonString = "{ \"splashes\": [ \"§4Flarial §fMan!\" ] }";
                    writer.WriteLine(jsonString);
                }

                await webClient.DownloadFileTaskAsync(new Uri("https://cdn-c6f.pages.dev/assets/flarial-title.png"),
                    Path.Combine(launcherPath, "Versions", version, "data", "resource_packs", "vanilla", "textures",
                        "ui", "title.png"));
                await webClient.DownloadFileTaskAsync(new Uri("https://cdn-c6f.pages.dev/assets/flarial_mogang.png"),
                    Path.Combine(launcherPath, "Versions", version, "UAP.Assets", "minecraft", "icons", "MCSplashScreen.scale-200.png"));
            }
        }


        public static async Task<bool> InstallMinecraft(string version)
        {
            MainWindow.progressPercentage = 0;
            string path = Path.Combine(launcherPath, "Versions", $"Minecraft{version}.Appx");

            bool ello = await DownloadApplication(version);

            if (ello)
            {
                Trace.WriteLine("Finished downloading the specified version's application bundle.");

                Minecraft.Init();

                if (Minecraft.Package != null)
                {
                    await BackupManager.CreateBackup("temp");
                    Trace.WriteLine("Uninstalling current Minecraft version.");

                }

                Trace.WriteLine("Deploying Minecraft's Application Bundle.");

                await ExtractAppxAsync(path, Path.Combine(launcherPath, "Versions", version), version);
                    
                await RemoveMinecraftPackage();

                if (await InstallAppBundle(Path.Combine(launcherPath, "Versions", version)) == false)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        CustomDialogBox MessageBox = new CustomDialogBox("Failed", "Failed to install.", "MessageBox");
                        MessageBox.ShowDialog();
                    });
                    
                    return false;
                }

                await Task.Delay(1);

                Trace.WriteLine("Temporary backup found, now loading.");
                await BackupManager.LoadBackup("temp");

                Trace.WriteLine("Temporary backup loaded, now deleting.");
                await BackupManager.DeleteBackup("temp");


                Trace.WriteLine("Installation complete.");
            }
            return ello;
        }
    }
}