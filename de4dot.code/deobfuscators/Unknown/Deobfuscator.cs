/*
    Copyright (C) 2011-2015 de4dot@gmail.com

    This file is part of de4dot.

    de4dot is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    de4dot is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with de4dot.  If not, see <http://www.gnu.org/licenses/>.
*/

using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace de4dot.code.deobfuscators.Unknown {
	public class DeobfuscatorInfo : DeobfuscatorInfoBase {
		public const string THE_NAME = "Unknown";
		public const string THE_TYPE = "un";
		const string DEFAULT_REGEX = DeobfuscatorBase.DEFAULT_VALID_NAME_REGEX;

		public DeobfuscatorInfo()
			: base(DEFAULT_REGEX) {
		}

		public override string Name => THE_NAME;
		public override string Type => THE_TYPE;

		public override IDeobfuscator CreateDeobfuscator() =>
			new Deobfuscator(new Deobfuscator.Options {
				RenameResourcesInCode = false,
				ValidNameRegex = validNameRegex.Get(),
			});
	}

	class Deobfuscator : DeobfuscatorBase {
		string obfuscatorName;

		internal class Options : OptionsBase {
		}

		public override string Type => DeobfuscatorInfo.THE_TYPE;
		public override string TypeLong => DeobfuscatorInfo.THE_NAME;
		public override string Name => obfuscatorName ?? "Unknown Obfuscator";

		internal Deobfuscator(Options options)
			: base(options) => KeepTypes = true;

		void SetName(string name) {
			if (obfuscatorName == null && name != null)
				obfuscatorName = $"{name} (not supported)";
		}

		protected override int DetectInternal() {
			SetName(ScanTypes());
			return 1;
		}

		protected override void ScanForObfuscator() {
		}

		string ScanTypes() {
			foreach (var type in module.Types) {
				var fn = type.FullName;
				if (fn == "ZYXDNGuarder")
					return "DNGuard HVM";
				if (type.Name.String.Contains("();\t"))
					return "Manco .NET Obfuscator";
				if (Regex.IsMatch(fn, @"^EMyPID_\d+_$"))
					return "BitHelmet Obfuscator";
				if (fn == "YanoAttribute")
					return "Yano Obfuscator";
			}
			return null;
		}

		public override IEnumerable<int> GetStringDecrypterMethods() => new List<int>();
	}
}
