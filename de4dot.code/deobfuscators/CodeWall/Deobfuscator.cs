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

using System;
using System.Collections.Generic;
using Mono.Cecil;
using Mono.MyStuff;
using de4dot.blocks;
using de4dot.PE;

namespace de4dot.code.deobfuscators.CodeWall {
	public class DeobfuscatorInfo : DeobfuscatorInfoBase {
		public const string THE_NAME = "CodeWall";
		public const string THE_TYPE = "cw";

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
		MethodsDecrypter methodsDecrypter;

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
		}

		protected override int detectInternal() {
			int val = 0;

			int sum = toInt32(methodsDecrypter.Detected);
			if (sum > 0)
				val += 100 + 10 * (sum - 1);

			return val;
		}

		protected override void scanForObfuscator() {
			methodsDecrypter = new MethodsDecrypter(module);
			methodsDecrypter.find();
		}

		public override bool getDecryptedModule(ref byte[] newFileData, ref DumpedMethods dumpedMethods) {
			if (!methodsDecrypter.Detected)
				return false;

			byte[] fileData = ModuleBytes ?? DeobUtils.readModule(module);
			var peImage = new PeImage(fileData);

			if (!methodsDecrypter.decrypt(peImage, ref dumpedMethods))
				return false;

			newFileData = fileData;
			return true;
		}

		public override IDeobfuscator moduleReloaded(ModuleDefinition module) {
			var newOne = new Deobfuscator(options);
			newOne.setModule(module);
			newOne.methodsDecrypter = new MethodsDecrypter(module, methodsDecrypter);
			return newOne;
		}

		public override void deobfuscateBegin() {
			base.deobfuscateBegin();
			addAssemblyReferenceToBeRemoved(methodsDecrypter.AssemblyNameReference, "Obfuscator decrypter DLL reference");
		}

		public override void deobfuscateMethodEnd(Blocks blocks) {
			methodsDecrypter.deobfuscate(blocks);
			base.deobfuscateMethodEnd(blocks);
		}

		public override void deobfuscateEnd() {
			base.deobfuscateEnd();
		}

		public override IEnumerable<int> getStringDecrypterMethods() {
			var list = new List<int>();
			//TODO:
			return list;
		}
	}
}
