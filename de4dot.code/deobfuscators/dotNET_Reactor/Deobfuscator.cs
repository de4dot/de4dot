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
using de4dot.blocks;

namespace de4dot.deobfuscators.dotNET_Reactor {
	class DeobfuscatorInfo : DeobfuscatorInfoBase {
		public DeobfuscatorInfo()
			: base("dr") {
		}

		internal static string ObfuscatorType {
			get { return "dotNetReactor"; }
		}

		public override string Type {
			get { return ObfuscatorType; }
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

		PE.PeImage peImage;
		byte[] fileData;
		MethodsDecrypter methodsDecrypter;
		StringDecrypter stringDecrypter;

		internal class Options : OptionsBase {
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

			if (methodsDecrypter.Detected)
				val += 100;
			else if (stringDecrypter.Detected)
				val += 100;
			if (methodsDecrypter.Detected && stringDecrypter.Detected)
				val += 10;

			return val;
		}

		protected override void scanForObfuscator() {
			methodsDecrypter = new MethodsDecrypter(module);
			methodsDecrypter.find();
			stringDecrypter = new StringDecrypter(module);
			stringDecrypter.find();
		}

		public override byte[] getDecryptedModule() {
			using (var fileStream = new FileStream(module.FullyQualifiedName, FileMode.Open, FileAccess.Read, FileShare.Read)) {
				fileData = new byte[(int)fileStream.Length];
				fileStream.Read(fileData, 0, fileData.Length);
			}
			peImage = new PE.PeImage(fileData);

			if (!methodsDecrypter.decrypt(peImage, DeobfuscatedFile))
				return null;

			return fileData;
		}

		public override IDeobfuscator moduleReloaded(ModuleDefinition module) {
			var newOne = new Deobfuscator(options);
			newOne.setModule(module);
			newOne.peImage = new PE.PeImage(fileData);
			newOne.methodsDecrypter = new MethodsDecrypter(module, methodsDecrypter);
			newOne.stringDecrypter = new StringDecrypter(module, stringDecrypter);
			return newOne;
		}

		public override void deobfuscateBegin() {
			base.deobfuscateBegin();

			stringDecrypter.init(peImage, DeobfuscatedFile);

			foreach (var info in stringDecrypter.DecrypterInfos) {
				staticStringDecrypter.add(info.method, (method2, args) => {
					return stringDecrypter.decrypt(method2, (int)args[0]);
				});
			}
			DeobfuscatedFile.stringDecryptersAdded();
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
