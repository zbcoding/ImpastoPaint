//
// UpdateChecker.cs
//
// Author:
//       zbcoding
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
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Pinta.Actions;

/// <summary>Comparison result for the latest GitHub release vs. the running app.</summary>
internal sealed record ReleaseInfo (string TagName, string HtmlUrl);

/// <summary>
/// Checks the GitHub latest release and decides whether a newer version of
/// Impasto is available than the one currently running.
/// </summary>
internal static class UpdateChecker
{
	private const string Owner = "zbcoding";
	private const string Repo = "ImpastoPaint";
	private const string LatestReleaseApi = $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";

	/// <summary>
	/// Returns the newest release on GitHub, or <see langword="null"/> if it
	/// can't be determined (e.g. no network connection).
	/// </summary>
	public static async Task<ReleaseInfo?> GetLatestReleaseAsync ()
	{
		try {
			using var client = new HttpClient ();
			// GitHub's API requires a User-Agent header.
			client.DefaultRequestHeaders.UserAgent.ParseAdd ("ImpastoPaint");

			using var response = await client.GetAsync (LatestReleaseApi);
			response.EnsureSuccessStatusCode ();

			string json = await response.Content.ReadAsStringAsync ();
			using JsonDocument doc = JsonDocument.Parse (json);
			JsonElement root = doc.RootElement;

			string? tag = root.TryGetProperty ("tag_name", out JsonElement tag_elem)
				? tag_elem.GetString ()
				: null;

			string? url = root.TryGetProperty ("html_url", out JsonElement url_elem)
				? url_elem.GetString ()
				: null;

			if (string.IsNullOrEmpty (tag))
				return null;

			return new ReleaseInfo (tag, url ?? string.Empty);
		} catch {
			// Offline or unreachable - never block startup on this.
			return null;
		}
	}

	/// <summary>
	/// True if <paramref name="latestTag"/> (e.g. "v0.1.0") is a newer release
	/// than the currently running <paramref name="currentVersion"/>.
	/// </summary>
	public static bool IsNewer (string latestTag, string currentVersion)
	{
		if (!TryParseVersion (latestTag, out Version? latest))
			return false;
		if (!TryParseVersion (currentVersion, out Version? current))
			return false;

		return latest > current;
	}

	/// <summary>
	/// Extracts the numeric "major.minor.build" portion from a version string
	/// that may contain decorations such as a leading "v" or a trailing "f".
	/// </summary>
	private static bool TryParseVersion (string value, out Version? version)
	{
		version = null;
		if (string.IsNullOrEmpty (value))
			return false;

		Span<char> numeric = stackalloc char[value.Length];
		int length = 0;
		foreach (char c in value) {
			if (char.IsAsciiDigit (c) || c == '.')
				numeric[length++] = c;
		}

		return length > 0 && Version.TryParse (numeric[..length].ToString (), out version);
	}
}
