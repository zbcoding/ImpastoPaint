//
// AddinSetupService.cs
//
// Author:
//       Lluis Sanchez Gual <lluis@novell.com>
//
// Copyright (c) 2011 Novell, Inc (http://www.novell.com)
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

using System;
using System.Net;
using System.Net.Http;
using Mono.Addins;
using Mono.Addins.Setup;
using Pinta.Core;

namespace Pinta;

public sealed class AddinSetupService : SetupService
{
	internal AddinSetupService (AddinRegistry r) : base (r)
	{
		Mono.Addins.Setup.HttpClientProvider.SetHttpClientFactory (CreateHttpClient);
	}

	// Work around a bug (#1542) in Mono.Addins.Setup.HttpClientDownloadFileRequest,
	// which assumes that ContentLength is never null.
	// Github's server (which hosts the repo) doesn't provide this for gzipped responses.
	private static readonly HttpClientHandler shared_handler = new () {
		AutomaticDecompression = DecompressionMethods.Deflate
	};

	private static HttpClient CreateHttpClient (string uri)
	{
		// Refreshing the add-in list is latency-bound, not bandwidth-bound: the repository
		// indexes are a couple of KB each, but every one costs a TCP connect plus a TLS
		// handshake. Sharing the handler keeps its connection pool alive across the whole
		// refresh so only the first request pays for that.
		// disposeHandler: false because Mono.Addins owns the returned client and may dispose it.
		return new HttpClient (shared_handler, disposeHandler: false);
	}

	public bool AreRepositoriesRegistered ()
	{
		string url = GetPlatformRepositoryUrl ();
		return Repositories.ContainsRepository (url);
	}

	public void RegisterRepositories (bool enable)
	{
		RemoveLegacyRepositories ();

		RegisterRepository (GetPlatformRepositoryUrl (),
				    Translations.GetString ("Pinta Community Addins - Platform-Specific"),
				    enable);

		RegisterRepository (GetAllRepositoryUrl (),
				    Translations.GetString ("Pinta Community Addins - Cross-Platform"),
				    enable);
	}

	private void RegisterRepository (string url, string name, bool enable)
	{
		if (Repositories.ContainsRepository (url))
			return;

		var rep = Repositories.RegisterRepository (null, url, false);
		rep.Name = name;
		// Although repositories are enabled by default, we should always call this method to ensure
		// that the repository name from the previous line ends up being saved to disk.
		Repositories.SetRepositoryEnabled (url, enable);
	}

	// The github.io host answers every request with a 301 to this one, so addressing it
	// directly saves a connect and a handshake per file fetched.
	private const string RepositoryBaseUrl = "https://www.pinta-project.com/Pinta-Community-Addins/repository/";

	// What the same repositories were registered as before that redirect was cut out.
	private const string LegacyRepositoryBaseUrl = "http://pintaproject.github.io/Pinta-Community-Addins/repository/";

	private static string GetPlatformRepositoryUrl ()
	{
		string platform = SystemManager.GetOperatingSystem () switch {
			OS.Windows => "Windows",
			OS.Mac => "Mac",
			_ => "Linux"
		};

		return RepositoryBaseUrl + platform + "/main.mrep";
	}

	private static string GetAllRepositoryUrl ()
	{
		return RepositoryBaseUrl + "All/main.mrep";
	}

	/// <summary>
	/// Drops repository registrations that point at the pre-redirect URL, so an existing
	/// install stops paying for the redirect and does not list each repository twice.
	/// </summary>
	private void RemoveLegacyRepositories ()
	{
		foreach (AddinRepository rep in Repositories.GetRepositories ()) {
			if (rep.Url is not null && rep.Url.StartsWith (LegacyRepositoryBaseUrl, StringComparison.Ordinal))
				Repositories.RemoveRepository (rep.Url);
		}
	}
}
