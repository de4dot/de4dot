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
using de4dot.blocks;

namespace de4dot.code.deobfuscators.Rummage {
	public class DeobfuscatorInfo : DeobfuscatorInfoBase {
		public const string THE_NAME = "Rummage";
		public const string THE_TYPE = "rm";
		const string DEFAULT_REGEX = @"!.";

		public DeobfuscatorInfo()
			: base(DEFAULT_REGEX) {
		}

		public override string Name => THE_NAME;
		public override string Type => THE_TYPE;

		public override IDeobfuscator CreateDeobfuscator() =>
			new Deobfuscator(new Deobfuscator.Options {
				ValidNameRegex = validNameRegex.Get(),
			});
	}

	class Deobfuscator : DeobfuscatorBase {
		string obfuscatorName = DeobfuscatorInfo.THE_NAME;
		StringDecrypter stringDecrypter;

		internal class Options : OptionsBase {
		}

		public override string Type => DeobfuscatorInfo.THE_TYPE;
		public override string TypeLong => DeobfuscatorInfo.THE_NAME;
		public override string Name => obfuscatorName;

		public Deobfuscator(Options options)
			: base(options) {
		}

		protected override int DetectInternal() {
			int val = 0;

			int sum = ToInt32(stringDecrypter.Detected);
			if (sum > 0)
				val += 100 + 10 * (sum - 1);

			return val;
		}

		protected override void ScanForObfuscator() {
			stringDecrypter = new StringDecrypter(module);
			stringDecrypter.Find();
			DetectVersion();
		}

		void DetectVersion() {
			string version;
			switch (stringDecrypter.Version) {
			case RummageVersion.V1_1_445: version = "v1.1 - v2.0"; break;
			case RummageVersion.V2_1_729: version = "v2.1+"; break;
			default: version = null; break;
			}
			if (version != null)
				obfuscatorName += " " + version;
		}

		public override void DeobfuscateBegin() {
			base.DeobfuscateBegin();

			stringDecrypter.Initialize();
		}


		public override void DeobfuscateMethodEnd(Blocks blocks) {
			if (CanRemoveStringDecrypterType)
				stringDecrypter.Deobfuscate(blocks);
			base.DeobfuscateMethodEnd(blocks);
		}

		public override void DeobfuscateEnd() {
			if (CanRemoveStringDecrypterType) {
				AddTypeToBeRemoved(stringDecrypter.Type, "String decrypter type");
				AddTypesToBeRemoved(stringDecrypter.OtherTypes, "Decrypted string type");
			}
			base.DeobfuscateEnd();
		}

		public override IEnumerable<int> GetStringDecrypterMethods() => new List<int>();
	}
}
