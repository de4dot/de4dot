/*
    Copyright (C) 2011-2013 de4dot@gmail.com

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
using dnlib.DotNet;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.Skater_NET {
	public class DeobfuscatorInfo : DeobfuscatorInfoBase {
		public const string THE_NAME = "Skater .NET";
		public const string THE_TYPE = "sk";
		const string DEFAULT_REGEX = @"!`[^0-9]+&" + DeobfuscatorBase.DEFAULT_VALID_NAME_REGEX;

		public DeobfuscatorInfo()
			: base(DEFAULT_REGEX) {
		}

		public override string Name {
			get { return THE_NAME; }
		}

		public override string Type {
			get { return THE_TYPE; }
		}

		public override IDeobfuscator createDeobfuscator() {
			return new Deobfuscator(new Deobfuscator.Options {
				ValidNameRegex = validNameRegex.get(),
			});
		}
	}

	class Deobfuscator : DeobfuscatorBase {
		Options options;

		StringDecrypter stringDecrypter;
		EnumClassFinder enumClassFinder;

		internal class Options : OptionsBase {
		}

		public override string Type {
			get { return DeobfuscatorInfo.THE_TYPE; }
		}

		public override string TypeLong {
			get { return DeobfuscatorInfo.THE_NAME; }
		}

		public override string Name {
			get { return DeobfuscatorInfo.THE_NAME; }
		}

		public Deobfuscator(Options options)
			: base(options) {
			this.options = options;
			StringFeatures = StringFeatures.AllowNoDecryption | StringFeatures.AllowStaticDecryption;
		}

		protected override int detectInternal() {
			int val = 0;

			if (stringDecrypter.Detected)
				val += 100;

			return val;
		}

		protected override void scanForObfuscator() {
			stringDecrypter = new StringDecrypter(module);

			if (hasAssemblyRef("Microsoft.VisualBasic"))
				stringDecrypter.find();
		}

		bool hasAssemblyRef(string name) {
			foreach (var asmRef in module.GetAssemblyRefs()) {
				if (asmRef.Name == name)
					return true;
			}
			return false;
		}

		public override void deobfuscateBegin() {
			base.deobfuscateBegin();

			enumClassFinder = new EnumClassFinder(module);

			stringDecrypter.initialize(DeobfuscatedFile);
		}

		public override void deobfuscateMethodEnd(Blocks blocks) {
			if (CanRemoveStringDecrypterType)
				stringDecrypter.deobfuscate(blocks);
			enumClassFinder.deobfuscate(blocks);
			base.deobfuscateMethodEnd(blocks);
		}

		public override void deobfuscateEnd() {
			if (Operations.DecryptStrings != OpDecryptString.None && stringDecrypter.CanRemoveType)
				addTypeToBeRemoved(stringDecrypter.Type, "String decrypter type");
			fixEnumTypes();

			base.deobfuscateEnd();
		}

		public override IEnumerable<int> getStringDecrypterMethods() {
			var list = new List<int>();
			return list;
		}
	}
}
