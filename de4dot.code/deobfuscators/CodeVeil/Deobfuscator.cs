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
using Mono.MyStuff;
using de4dot.blocks.cflow;

namespace de4dot.code.deobfuscators.CodeVeil {
	public class DeobfuscatorInfo : DeobfuscatorInfoBase {
		public const string THE_NAME = "CodeVeil";
		public const string THE_TYPE = "cv";

		public DeobfuscatorInfo()
			: base() {
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

		protected override IEnumerable<Option> getOptionsInternal() {
			return new List<Option>() {
			};
		}
	}

	class Deobfuscator : DeobfuscatorBase {
		Options options;
		string obfuscatorName = DeobfuscatorInfo.THE_NAME;
		bool foundKillType = false;

		MethodsDecrypter methodsDecrypter;
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
			get { return obfuscatorName; }
		}

		public Deobfuscator(Options options)
			: base(options) {
			this.options = options;
		}

		protected override int detectInternal() {
			int val = 0;

			int sum = toInt32(foundKillType) +
					toInt32(methodsDecrypter.Detected) +
					toInt32(stringDecrypter.Detected);
			if (sum > 0)
				val += 100 + 10 * (sum - 1);

			return val;
		}

		protected override void scanForObfuscator() {
			findKillType();
			methodsDecrypter = new MethodsDecrypter(module);
			methodsDecrypter.find();
			stringDecrypter = new StringDecrypter(module);
			stringDecrypter.find();
		}

		void findKillType() {
			foreach (var type in module.Types) {
				if (type.FullName == "____KILL") {
					addTypeToBeRemoved(type, "KILL type");
					foundKillType = true;
					break;
				}
			}
		}

		public override bool getDecryptedModule(ref byte[] newFileData, ref Dictionary<uint, DumpedMethod> dumpedMethods) {
			if (!methodsDecrypter.Detected)
				return false;

			var fileData = DeobUtils.readModule(module);
			if (!methodsDecrypter.decrypt(fileData, ref dumpedMethods))
				return false;

			newFileData = fileData;
			return true;
		}

		public override IDeobfuscator moduleReloaded(ModuleDefinition module) {
			var newOne = new Deobfuscator(options);
			newOne.setModule(module);
			newOne.methodsDecrypter = new MethodsDecrypter(module, methodsDecrypter);
			newOne.stringDecrypter = new StringDecrypter(module, stringDecrypter);
			return newOne;
		}

		public override void deobfuscateBegin() {
			base.deobfuscateBegin();


			if (Operations.DecryptStrings != OpDecryptString.None) {
				stringDecrypter.initialize();
				staticStringInliner.add(stringDecrypter.DecryptMethod, (method, args) => {
					return stringDecrypter.decrypt((int)args[0]);
				});
				DeobfuscatedFile.stringDecryptersAdded();
			}

			//TODO:
		}

		public override void deobfuscateEnd() {
			//TODO:

			base.deobfuscateEnd();
		}

		public override IEnumerable<string> getStringDecrypterMethods() {
			var list = new List<string>();
			//TODO:
			return list;
		}
	}
}
