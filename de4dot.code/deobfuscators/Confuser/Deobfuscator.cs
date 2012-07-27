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
using de4dot.blocks.cflow;
using de4dot.PE;

namespace de4dot.code.deobfuscators.Confuser {
	public class DeobfuscatorInfo : DeobfuscatorInfoBase {
		public const string THE_NAME = "Confuser";
		public const string THE_TYPE = "cn";

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

		JitMethodsDecrypter jitMethodsDecrypter;
		ProxyCallFixer proxyCallFixer;

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

		public override IEnumerable<IBlocksDeobfuscator> BlocksDeobfuscators {
			get {
				var list = new List<IBlocksDeobfuscator>();
				list.Add(new ConstantsFolder { ExecuteOnNoChange = true });
				return list;
			}
		}

		public Deobfuscator(Options options)
			: base(options) {
			this.options = options;
		}

		protected override int detectInternal() {
			int val = 0;

			int sum = toInt32(jitMethodsDecrypter.Detected) +
					toInt32(proxyCallFixer != null ? proxyCallFixer.Detected : false);
			if (sum > 0)
				val += 100 + 10 * (sum - 1);

			return val;
		}

		protected override void scanForObfuscator() {
			jitMethodsDecrypter = new JitMethodsDecrypter(module, DeobfuscatedFile);
			jitMethodsDecrypter.find();
			if (jitMethodsDecrypter.Detected)
				return;
			initTheRest();
		}

		void initTheRest() {
			proxyCallFixer = new ProxyCallFixer(module, getFileData(), DeobfuscatedFile);
			proxyCallFixer.findDelegateCreator();
		}

		byte[] getFileData() {
			if (ModuleBytes != null)
				return ModuleBytes;
			return ModuleBytes = DeobUtils.readModule(module);
		}

		public override bool getDecryptedModule(int count, ref byte[] newFileData, ref DumpedMethods dumpedMethods) {
			if (count != 0 || !jitMethodsDecrypter.Detected)
				return false;

			byte[] fileData = getFileData();
			var peImage = new PeImage(fileData);

			if (jitMethodsDecrypter.Detected) {
				jitMethodsDecrypter.initialize();
				if (!jitMethodsDecrypter.decrypt(peImage, fileData, ref dumpedMethods))
					return false;

				newFileData = fileData;
				return true;
			}

			return false;
		}

		public override IDeobfuscator moduleReloaded(ModuleDefinition module) {
			var newOne = new Deobfuscator(options);
			newOne.DeobfuscatedFile = DeobfuscatedFile;
			newOne.setModule(module);
			newOne.jitMethodsDecrypter = new JitMethodsDecrypter(module, jitMethodsDecrypter);
			newOne.initTheRest();
			newOne.ModuleBytes = ModuleBytes;
			return newOne;
		}

		public override void deobfuscateBegin() {
			base.deobfuscateBegin();

			proxyCallFixer.find();
		}

		public override void deobfuscateMethodEnd(Blocks blocks) {
			proxyCallFixer.deobfuscate(blocks);
			base.deobfuscateMethodEnd(blocks);
		}

		public override void deobfuscateEnd() {
			removeProxyDelegates(proxyCallFixer);

			base.deobfuscateEnd();
		}

		public override IEnumerable<int> getStringDecrypterMethods() {
			var list = new List<int>();
			return list;
		}
	}
}
