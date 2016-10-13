﻿using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using GitHub.Extensions;
using GitHub.Primitives;
using LibGit2Sharp;

namespace GitHub.Services
{
    [Export(typeof(IGitClient))]
    [PartCreationPolicy(CreationPolicy.Shared)]
    public class GitClient : IGitClient
    {
        readonly PushOptions pushOptions;
        readonly FetchOptions fetchOptions;

        [ImportingConstructor]
        public GitClient(IGitHubCredentialProvider credentialProvider)
        {
            pushOptions = new PushOptions { CredentialsProvider = credentialProvider.HandleCredentials };
            fetchOptions = new FetchOptions { CredentialsProvider = credentialProvider.HandleCredentials };
        }

        public Task Push(IRepository repository, string branchName, string remoteName)
        {
            Guard.ArgumentNotEmptyString(branchName, nameof(branchName));
            Guard.ArgumentNotEmptyString(remoteName, nameof(remoteName));

            return Task.Factory.StartNew(() =>
            {
                if (repository.Head?.Commits != null && repository.Head.Commits.Any())
                {
                    var remote = repository.Network.Remotes[remoteName];
                    repository.Network.Push(remote, "HEAD", @"refs/heads/" + branchName, pushOptions);
                }
            });
        }

        public Task Fetch(IRepository repository, string remoteName)
        {
            Guard.ArgumentNotEmptyString(remoteName, nameof(remoteName));

            return Task.Factory.StartNew(() =>
            {
                var remote = repository.Network.Remotes[remoteName];
                repository.Network.Fetch(remote, fetchOptions);
            });
        }

        public Task Fetch(IRepository repository, string remoteName, params string[] refspecs)
        {
            Guard.ArgumentNotEmptyString(remoteName, nameof(remoteName));

            return Task.Factory.StartNew(() =>
            {
                var remote = repository.Network.Remotes[remoteName];
                repository.Network.Fetch(remote, refspecs, fetchOptions);
            });
        }

        public Task Checkout(IRepository repository, string branchName)
        {
            Guard.ArgumentNotEmptyString(branchName, nameof(branchName));

            return Task.Factory.StartNew(() =>
            {
                repository.Checkout(branchName);
            });
        }

        public Task SetConfig(IRepository repository, string key, string value)
        {
            Guard.ArgumentNotEmptyString(key, nameof(key));
            Guard.ArgumentNotEmptyString(value, nameof(value));

            return Task.Factory.StartNew(() =>
            {
                repository.Config.Set(key, value);
            });
        }

        public Task SetRemote(IRepository repository, string remoteName, Uri url)
        {
            Guard.ArgumentNotEmptyString(remoteName, nameof(remoteName));

            return Task.Factory.StartNew(() =>
            {
                repository.Config.Set("remote." + remoteName + ".url", url.ToString());
                repository.Config.Set("remote." + remoteName + ".fetch", "+refs/heads/*:refs/remotes/" + remoteName + "/*");
            });
        }

        public Task SetTrackingBranch(IRepository repository, string branchName, string remoteName)
        {
            Guard.ArgumentNotEmptyString(branchName, nameof(branchName));
            Guard.ArgumentNotEmptyString(remoteName, nameof(remoteName));

            return Task.Factory.StartNew(() =>
            {
                var remoteBranchName = IsCanonical(remoteName) ? remoteName : "refs/remotes/" + remoteName + "/" + branchName;
                var remoteBranch = repository.Branches[remoteBranchName];
                // if it's null, it's because nothing was pushed
                if (remoteBranch != null)
                {
                    var localBranchName = IsCanonical(branchName) ? branchName : "refs/heads/" + branchName;
                    var localBranch = repository.Branches[localBranchName];
                    repository.Branches.Update(localBranch, b => b.TrackedBranch = remoteBranch.CanonicalName);
                }
            });
        }

        public Task UnsetConfig(IRepository repository, string key)
        {
            Guard.ArgumentNotEmptyString(key, nameof(key));

            return Task.Factory.StartNew(() =>
            {
                repository.Config.Unset(key);
            });
        }

        public Task<Remote> GetHttpRemote(IRepository repo, string remote)
        {
            return Task.Factory.StartNew(() =>
            {
                var uri = GitService.GitServiceHelper.GetRemoteUri(repo, remote);
                var remoteName = uri.IsHypertextTransferProtocol ? remote : remote + "-http";
                var ret = repo.Network.Remotes[remoteName];
                if (ret == null)
                    ret = repo.Network.Remotes.Add(remoteName, UriString.ToUriString(uri.ToRepositoryUrl()));
                return ret;
            });
        }

        public async Task<string> ExtractFile(IRepository repository, string commitSha, string fileName)
        {
            if (repository.Head.Tip.Sha == commitSha && repository.RetrieveStatus()[fileName].State == FileStatus.Unaltered)
            {
                return Path.Combine(repository.Info.Path, fileName);
            }
            else
            {
                var commit = repository.Lookup<Commit>(commitSha);
                var blob = commit[fileName]?.Target as Blob;

                var tempFile = Path.Combine(
                    Path.GetTempPath(),
                    Guid.NewGuid().ToString() + Path.GetExtension(fileName));

                if (blob != null)
                {
                    using (var source = blob.GetContentStream(new FilteringOptions(fileName)))
                    using (var destination = File.OpenWrite(tempFile))
                    {
                        await source.CopyToAsync(destination);
                    }
                }
                else
                {
                    File.Create(tempFile).Dispose();
                }

                return tempFile;
            }
        }

        static bool IsCanonical(string s)
        {
            return s.StartsWith("refs/", StringComparison.Ordinal);
        }
    }
}
