﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using TACT.Net.Cryptography;

namespace TACT.Net.Network
{
    public sealed class CDNClient
    {
        public readonly List<string> Hosts;
        public readonly Armadillo Armadillo;
        /// <summary>
        /// Applies Armadillo decryption to CDN responses
        /// </summary>
        public bool ApplyDecryption { get; set; }

        private int _hostIndex = 0;

        #region Constructors

        /// <summary>
        /// Creates a new CDN Client without any hosts
        /// </summary>
        /// <param name="applyDecryption"></param>
        private CDNClient(bool applyDecryption)
        {
            Hosts = new List<string>();
            Armadillo = new Armadillo();
            ApplyDecryption = applyDecryption;
        }

        /// <summary>
        /// Creates a new CDN Client and loads the hosts from the CDNs file
        /// </summary>
        /// <param name="configContainer"></param>
        /// <param name="applyDecryption"></param>
        public CDNClient(Configs.ConfigContainer configContainer, bool applyDecryption = false) : this(applyDecryption)
        {
            if (configContainer.CDNsFile == null)
                throw new ArgumentException("Unable to load CDNs file");

            string[] hosts = configContainer.CDNsFile.GetValue("Hosts", configContainer.Locale)?.Split(' ');
            foreach (var host in hosts)
                Hosts.Add(host.Split('?')[0]);

            if (Hosts.Count == 0)
                throw new FormatException("No hosts found");
        }

        /// <summary>
        /// Creates a new CDN Client and uses the provided hosts
        /// </summary>
        /// <param name="hosts"></param>
        /// <param name="applyDecryption"></param>
        public CDNClient(IEnumerable<string> hosts, bool applyDecryption = false) : this(applyDecryption)
        {
            foreach (var host in hosts)
                Hosts.Add(host.Split('?')[0]);

            if (Hosts.Count == 0)
                throw new ArgumentException("No hosts found");
        }

        #endregion

        #region Methods

        /// <summary>
        /// Attempts to return a stream to a file from Blizzard's CDN
        /// </summary>
        /// <param name="cdnpath">CDN file path excluding the host</param>
        /// <returns></returns>
        public async Task<Stream> OpenStream(string cdnpath)
        {
            // used as a 404 check
            if (await GetContentLength(cdnpath) == -1)
                return null;

            foreach (var host in GetHosts())
            {
                HttpWebRequest req = WebRequest.CreateHttp("http://" + host + "/" + cdnpath);

                try
                {
                    using (var resp = (HttpWebResponse)await req.GetResponseAsync().ConfigureAwait(false))
                    {
                        var respStream = resp.GetResponseStream();
                        return ApplyDecryption ? Armadillo.Decrypt(cdnpath, respStream) : respStream;
                    }
                }
                catch (WebException) { }
            }

            return null;
        }

        /// <summary>
        /// Attempts to download a file from Blizzard's CDN
        /// </summary>
        /// <param name="cdnpath">CDN file path excluding the host</param>
        /// <param name="filepath">File save location</param>
        /// <returns></returns>
        public async Task<bool> DownloadFile(string cdnpath, string filepath)
        {
            // used as a 404 check
            if (await GetContentLength(cdnpath) == -1)
                return false;

            foreach (var host in GetHosts())
            {
                try
                {
                    HttpWebRequest req = WebRequest.CreateHttp("http://" + host + "/" + cdnpath);

                    using (var resp = (HttpWebResponse)await req.GetResponseAsync().ConfigureAwait(false))
                    using (var stream = resp.GetResponseStream())
                    using (var fs = File.Create(filepath))
                    {
                        var respStream = ApplyDecryption ? Armadillo.Decrypt(cdnpath, stream) : stream;
                        await respStream.CopyToAsync(fs).ConfigureAwait(false);

                        return true;
                    }
                }
                catch (WebException) { }
            }

            return false;
        }

        /// <summary>
        /// Returns the size of a file or -1 if it is inaccessible
        /// </summary>
        /// <param name="cdnpath"></param>
        /// <returns></returns>
        public async Task<long> GetContentLength(string cdnpath)
        {
            foreach (var host in GetHosts())
            {
                try
                {
                    HttpWebRequest req = WebRequest.CreateHttp("http://" + host + "/" + cdnpath);
                    req.Method = "HEAD";

                    using (var resp = (HttpWebResponse)await req.GetResponseAsync().ConfigureAwait(false))
                        if (resp.StatusCode == HttpStatusCode.OK)
                            return resp.ContentLength;
                }
                catch (WebException) { }
            }

            return -1;
        }

        /// <summary>
        /// Attempts to load an Armadillo key. Supports both filepaths and Battle.Net key directory lookups
        /// </summary>
        /// <param name="filePathOrKeyName"></param>
        /// <returns></returns>
        public bool SetDecryptionKey(string filePathOrKeyName) => Armadillo.SetKey(filePathOrKeyName);

        #endregion

        #region Helpers

        /// <summary>
        /// Iterates the hosts in order only progressing on network exception
        /// </summary>
        /// <returns></returns>
        private IEnumerable<string> GetHosts()
        {
            for (int i = 0; i < Hosts.Count; i++)
            {
                if (i != 0)
                    _hostIndex = ++_hostIndex % Hosts.Count;

                yield return Hosts[_hostIndex];
            }
        }

        #endregion
    }
}
