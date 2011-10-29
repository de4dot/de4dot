/*
    Copyright (C) 2011 de4dot@gmail.com

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

using System.IO;
using System.Collections.Generic;
using Mono.Cecil;
using Mono.MyStuff;
using de4dot.blocks;

namespace de4dot.deobfuscators.dotNET_Reactor {
	class DeobfuscatorInfo : DeobfuscatorInfoBase {
		BoolOption decryptMethods;
		BoolOption decryptBools;

		public DeobfuscatorInfo()
			: base("dr") {
			decryptMethods = new BoolOption(null, makeArgName("methods"), "Decrypt methods", true);
			decryptBools = new BoolOption(null, makeArgName("bools"), "Decrypt booleans", true);
		}

		internal static string ObfuscatorType {
			get { return "dotNet_Reactor"; }
		}

		public override string Type {
			get { return ObfuscatorType; }
		}

		public override IDeobfuscator createDeobfuscator() {
			return new Deobfuscator(new Deobfuscator.Options {
				ValidNameRegex = validNameRegex.get(),
				DecryptMethods = decryptMethods.get(),
				DecryptBools = decryptBools.get(),
			});
		}

		protected override IEnumerable<Option> getOptionsInternal() {
			return new List<Option>() {
				decryptMethods,
				decryptBools,
			};
		}
	}

	class Deobfuscator : DeobfuscatorBase {
		Options options;

		PE.PeImage peImage;
		byte[] fileData;
		MethodsDecrypter methodsDecrypter;
		StringDecrypter stringDecrypter;
		BooleanDecrypter booleanDecrypter;
		BoolValueInliner boolValueInliner;

		internal class Options : OptionsBase {
			public bool DecryptMethods { get; set; }
			public bool DecryptBools { get; set; }
		}

		public override string Type {
			get { return DeobfuscatorInfo.ObfuscatorType; }
		}

		public override string Name {
			get { return ".NET Reactor"; }
		}

		public Deobfuscator(Options options)
			: base(options) {
			this.options = options;
		}

		public override void init(ModuleDefinition module) {
			base.init(module);
		}

		protected override int detectInternal() {
			int val = 0;

			if (methodsDecrypter.Detected || stringDecrypter.Detected || booleanDecrypter.Detected)
				val += 100;

			int sum = convert(methodsDecrypter.Detected) +
					convert(stringDecrypter.Detected) +
					convert(booleanDecrypter.Detected);
			if (sum > 1)
				val += 10 * (sum - 1);

			return val;
		}

		static int convert(bool b) {
			return b ? 1 : 0;
		}

		protected override void scanForObfuscator() {
			methodsDecrypter = new MethodsDecrypter(module);
			methodsDecrypter.find();
			stringDecrypter = new StringDecrypter(module);
			stringDecrypter.find();
			booleanDecrypter = new BooleanDecrypter(module);
			booleanDecrypter.find();
		}

		public override bool getDecryptedModule(ref byte[] newFileData, ref Dictionary<uint, DumpedMethod> dumpedMethods) {
			using (var fileStream = new FileStream(module.FullyQualifiedName, FileMode.Open, FileAccess.Read, FileShare.Read)) {
				fileData = new byte[(int)fileStream.Length];
				fileStream.Read(fileData, 0, fileData.Length);
			}
			peImage = new PE.PeImage(fileData);

			if (!options.DecryptMethods)
				return false;

			if (!methodsDecrypter.decrypt(peImage, DeobfuscatedFile, ref dumpedMethods))
				return false;

			newFileData = fileData;
			return true;
		}

		public override IDeobfuscator moduleReloaded(ModuleDefinition module) {
			var newOne = new Deobfuscator(options);
			newOne.setModule(module);
			newOne.fileData = fileData;
			newOne.peImage = new PE.PeImage(fileData);
			newOne.methodsDecrypter = new MethodsDecrypter(module, methodsDecrypter);
			newOne.stringDecrypter = new StringDecrypter(module, stringDecrypter);
			newOne.booleanDecrypter = new BooleanDecrypter(module, booleanDecrypter);
			return newOne;
		}

		public override void deobfuscateBegin() {
			base.deobfuscateBegin();

			stringDecrypter.init(peImage, fileData, DeobfuscatedFile);
			booleanDecrypter.init(fileData, DeobfuscatedFile);
			boolValueInliner = new BoolValueInliner();

			if (options.DecryptBools) {
				boolValueInliner.add(booleanDecrypter.BoolDecrypterMethod, (method, args) => {
					return booleanDecrypter.decrypt((int)args[0]);
				});
			}

			foreach (var info in stringDecrypter.DecrypterInfos) {
				staticStringDecrypter.add(info.method, (method2, args) => {
					return stringDecrypter.decrypt(method2, (int)args[0]);
				});
			}
			if (stringDecrypter.OtherStringDecrypter != null) {
				staticStringDecrypter.add(stringDecrypter.OtherStringDecrypter, (method2, args) => {
					return stringDecrypter.decrypt((string)args[0]);
				});
			}
			DeobfuscatedFile.stringDecryptersAdded();

			if (Operations.DecryptStrings != OpDecryptString.None)
				addResourceToBeRemoved(stringDecrypter.StringsResource, "Encrypted strings");
			if (options.DecryptMethods) {
				addResourceToBeRemoved(methodsDecrypter.MethodsResource, "Encrypted methods");
				addCctorInitCallToBeRemoved(methodsDecrypter.MethodsDecrypterMethod);
			}
			if (options.DecryptBools)
				addResourceToBeRemoved(booleanDecrypter.BooleansResource, "Encrypted booleans");
			bool deleteTypes = Operations.DecryptStrings != OpDecryptString.None && options.DecryptMethods && options.DecryptBools;
			if (deleteTypes && methodsDecrypter.MethodsDecrypterMethod != null)
				addTypeToBeRemoved(methodsDecrypter.MethodsDecrypterMethod.DeclaringType, "Decrypter type");
		}

		public override bool deobfuscateOther(Blocks blocks) {
			if (boolValueInliner.HasHandlers)
				return boolValueInliner.decrypt(blocks) > 0;
			return false;
		}

		public override void deobfuscateMethodEnd(Blocks blocks) {
			base.deobfuscateMethodEnd(blocks);
		}

		public override void deobfuscateEnd() {
			base.deobfuscateEnd();
		}

		public override IEnumerable<string> getStringDecrypterMethods() {
			var list = new List<string>();
			foreach (var info in stringDecrypter.DecrypterInfos)
				list.Add(info.method.MetadataToken.ToInt32().ToString("X8"));
			return list;
		}
	}
}
