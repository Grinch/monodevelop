﻿//
// TextMatePlistFormat.cs
//
// Author:
//       Mike Krüger <mkrueger@xamarin.com>
//
// Copyright (c) 2016 Xamarin Inc. (http://xamarin.com)
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
using System.Collections.Generic;
using System.IO;
using MonoDevelop.Components;
using MonoDevelop.Ide.TypeSystem;

namespace MonoDevelop.Ide.Editor.Highlighting
{
	static class TextMateFormat
	{
		public static EditorTheme LoadEditorTheme (Stream stream)
		{
			if (stream == null)
				throw new ArgumentNullException (nameof (stream));
			var dictionary = PDictionary.FromStream (stream);
			var name = (PString)dictionary ["name"];
			var contentArray = dictionary ["settings"] as PArray;
			if (contentArray == null || contentArray.Count == 0)
				return new EditorTheme(name);
			
			var settings = new List<ThemeSetting> ();
			for (int i = 0; i < contentArray.Count; i++) {
				var dict = contentArray [i] as PDictionary;
				if (dict == null)
					continue;
				var themeSetting = LoadThemeSetting (dict);
				if (i == 0)
					themeSetting  = CalculateMissingColors (themeSetting);
				settings.Add (themeSetting);
			}
			var uuid = (PString)dictionary ["uuid"];

			return new EditorTheme (name, settings, uuid);
		}

		public static void Save (TextWriter writer, EditorTheme theme)
		{
			writer.WriteLine ("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
			writer.WriteLine ("<!DOCTYPE plist PUBLIC \"-//Apple Computer//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">");
			writer.WriteLine ("<plist version=\"1.0\">");
			writer.WriteLine ("<dict>");
			writer.WriteLine ("\t<key>name</key>");
			writer.WriteLine ("\t<string>" + Ambience.EscapeText (theme.Name) + "</string>");
			writer.WriteLine ("\t<key>settings</key>");
			writer.WriteLine ("\t<array>");
			foreach (var setting in theme.Settings) {
				writer.WriteLine ("\t\t<dict>");
				if (setting.Name != null) {
					writer.WriteLine ("\t\t\t<key>name</key>");
					writer.WriteLine ("\t\t\t<string>" + Ambience.EscapeText (setting.Name) + "</string>");
				}
				if (setting.Scopes.Count > 0) {
					writer.WriteLine ("\t\t\t<key>scope</key>");
					writer.WriteLine ("\t\t\t<string>" + Ambience.EscapeText (string.Join (", ", setting.Scopes)) + "</string>");
				}
				if (setting.Settings.Count > 0) {
					writer.WriteLine ("\t\t\t<key>settings</key>");
					writer.WriteLine ("\t\t\t<dict>");
					foreach (var kv in setting.Settings) {
						writer.WriteLine ("\t\t\t\t<key>" + Ambience.EscapeText (kv.Key) + "</key>");
						writer.WriteLine ("\t\t\t\t<string>" + Ambience.EscapeText (kv.Value) + "</string>");
					}
					writer.WriteLine ("\t\t\t</dict>");
				}
				writer.WriteLine ("\t\t</dict>");
			}
			writer.WriteLine ("\t</array>");
			writer.WriteLine ("\t<key>uuid</key>");
			writer.WriteLine ("\t<string>" + theme.Uuid +  "</string>");
			writer.WriteLine ("</dict>");
			writer.WriteLine ("</plist>");
		}

		static ThemeSetting LoadThemeSetting(PDictionary dict)
		{
			string name = null;
			var scopes = new List<string> ();
			var settings = new Dictionary<string, string> ();

			PObject val;
			if (dict.TryGetValue ("name", out val))
				name = ((PString)val).Value;
			if (dict.TryGetValue ("scope", out val)) {
				scopes.AddRange (((PString)val).Value.Split (new [] { ',' }, StringSplitOptions.RemoveEmptyEntries));
			}
			if (dict.TryGetValue ("settings", out val)) {
				var settingsDictionary = val as PDictionary;
				foreach (var setting in settingsDictionary) {
					settings.Add (setting.Key, ((PString)setting.Value).Value);
				}
			}

			return new ThemeSetting (name, scopes, settings);
		}

		static ThemeSetting CalculateMissingColors (ThemeSetting themeSetting)
		{
			var settings = (Dictionary<string, string>)themeSetting.Settings;
			settings [ThemeSettingColors.LineNumbersBackground] = HslColor.Parse (settings [ThemeSettingColors.Background]).AddLight (0.01).ToPangoString ();
			settings [ThemeSettingColors.LineNumbers] = HslColor.Parse (settings [ThemeSettingColors.Foreground]).AddLight (-0.1).ToPangoString ();

			return new ThemeSetting (themeSetting.Name, themeSetting.Scopes, settings);
		}
	}
}