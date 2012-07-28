/*
    Copyright (C) 2011-2012 de4dot@gmail.com

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
using Mono.Cecil;

namespace de4dot.code.deobfuscators.Xenocode {
	public class DeobfuscatorInfo : DeobfuscatorInfoBase {
		public const string THE_NAME = "Xenocode";
		public const string THE_TYPE = "xc";
		const string DEFAULT_REGEX = @"!^[oO01l]{4,}$&!^(get_|set_|add_|remove_|_)?x[a-f0-9]{16,}$&" + DeobfuscatorBase.DEFAULT_VALID_NAME_REGEX;
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
		bool foundXenocodeAttribute = false;
		StringDecrypter stringDecrypter;

		internal class Options : OptionsBase {
		}

		public override string Type {
			get { return DeobfuscatorInfo.THE_TYPE; }
		}

		public override string TypeLong {
			get { return DeobfuscatorInfo.THE_NAME; }
		}

		public override string Name {
			get { return TypeLong; }
		}

		public Deobfuscator(Options options)
			: base(options) {
		}

		protected override int detectInternal() {
			int val = 0;

			if (stringDecrypter.Detected)
				val += 100;
			if (foundXenocodeAttribute)
				val += 10;

			return val;
		}

		protected override void scanForObfuscator() {
			findXenocodeAttribute();
			stringDecrypter = new StringDecrypter(module);
			stringDecrypter.find();
		}

		void findXenocodeAttribute() {
			foreach (var type in module.Types) {
				switch (type.FullName) {
				case "Xenocode.Client.Attributes.AssemblyAttributes.ProcessedByXenocode":
				case "Xenocode.Client.Attributes.AssemblyAttributes.SuppressDisassembly":
				case "XenoCode.User.Attributes.AssemblyAttributes.ProcessedByXenoCode":
				case "XenoCode.User.Attributes.AssemblyAttributes.SuppressDisassembly":
					addAttributeToBeRemoved(type, "Obfuscator attribute");
					foundXenocodeAttribute = true;
					break;
				}
			}
		}

		public override void deobfuscateBegin() {
			base.deobfuscateBegin();

			staticStringInliner.add(stringDecrypter.Method, (method, gim, args) => stringDecrypter.decrypt((string)args[0], (int)args[1]));
			DeobfuscatedFile.stringDecryptersAdded();
		}

		public override void deobfuscateEnd() {
			if (CanRemoveStringDecrypterType)
				addTypeToBeRemoved(stringDecrypter.Type, "String decrypter type");
			base.deobfuscateEnd();
		}

		public override IEnumerable<int> getStringDecrypterMethods() {
			var list = new List<int>();
			if (stringDecrypter.Method != null)
				list.Add(stringDecrypter.Method.MetadataToken.ToInt32());
			return list;
		}
	}
}
