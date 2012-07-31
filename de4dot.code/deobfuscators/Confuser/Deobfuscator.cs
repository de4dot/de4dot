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
		BoolOption removeAntiDebug;
		BoolOption removeAntiDump;

		public DeobfuscatorInfo()
			: base() {
			removeAntiDebug = new BoolOption(null, makeArgName("antidb"), "Remove anti debug code", true);
			removeAntiDump = new BoolOption(null, makeArgName("antidump"), "Remove anti dump code", true);
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
				RemoveAntiDebug = removeAntiDebug.get(),
				RemoveAntiDump = removeAntiDump.get(),
			});
		}

		protected override IEnumerable<Option> getOptionsInternal() {
			return new List<Option>() {
				removeAntiDebug,
				removeAntiDump,
			};
		}
	}

	class Deobfuscator : DeobfuscatorBase {
		Options options;
		string obfuscatorName = DeobfuscatorInfo.THE_NAME;

		JitMethodsDecrypter jitMethodsDecrypter;
		MemoryMethodsDecrypter memoryMethodsDecrypter;
		ProxyCallFixer proxyCallFixer;
		ProxyCallFixerV1 proxyCallFixerV1;
		AntiDebugger antiDebugger;
		AntiDumping antiDumping;
		ResourceDecrypter resourceDecrypter;
		ConstantsDecrypter constantsDecrypter;
		Int32ValueInliner int32ValueInliner;
		Int64ValueInliner int64ValueInliner;
		SingleValueInliner singleValueInliner;
		DoubleValueInliner doubleValueInliner;
		StringDecrypter stringDecrypter;
		Unpacker unpacker;

		bool startedDeobfuscating = false;

		internal class Options : OptionsBase {
			public bool RemoveAntiDebug { get; set; }
			public bool RemoveAntiDump { get; set; }
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

				// Add this one last so all cflow is deobfuscated whenever it executes
				if (!startedDeobfuscating && int32ValueInliner != null)
					list.Add(new ConstantsInliner(int32ValueInliner, int64ValueInliner, singleValueInliner, doubleValueInliner) { ExecuteOnNoChange = true });

				return list;
			}
		}

		public Deobfuscator(Options options)
			: base(options) {
			this.options = options;
			StringFeatures = StringFeatures.AllowStaticDecryption | StringFeatures.AllowDynamicDecryption;
		}

		protected override int detectInternal() {
			int val = 0;

			int sum = toInt32(jitMethodsDecrypter != null ? jitMethodsDecrypter.Detected : false) +
					toInt32(memoryMethodsDecrypter != null ? memoryMethodsDecrypter.Detected : false) +
					toInt32(proxyCallFixer != null ? proxyCallFixer.Detected : false) +
					toInt32(proxyCallFixerV1 != null ? proxyCallFixerV1.Detected : false) +
					toInt32(antiDebugger != null ? antiDebugger.Detected : false) +
					toInt32(antiDumping != null ? antiDumping.Detected : false) +
					toInt32(resourceDecrypter != null ? resourceDecrypter.Detected : false) +
					toInt32(constantsDecrypter != null ? constantsDecrypter.Detected : false) +
					toInt32(stringDecrypter != null ? stringDecrypter.Detected : false) +
					toInt32(unpacker != null ? unpacker.Detected : false);
			if (sum > 0)
				val += 100 + 10 * (sum - 1);

			return val;
		}

		protected override void scanForObfuscator() {
			jitMethodsDecrypter = new JitMethodsDecrypter(module, DeobfuscatedFile);
			try {
				jitMethodsDecrypter.find();
			}
			catch {
			}
			if (jitMethodsDecrypter.Detected)
				return;
			memoryMethodsDecrypter = new MemoryMethodsDecrypter(module, DeobfuscatedFile);
			memoryMethodsDecrypter.find();
			if (memoryMethodsDecrypter.Detected)
				return;
			initTheRest();
		}

		void initTheRest() {
			resourceDecrypter = new ResourceDecrypter(module, DeobfuscatedFile);
			resourceDecrypter.find();
			constantsDecrypter = new ConstantsDecrypter(module, getFileData(), DeobfuscatedFile);
			constantsDecrypter.find();
			if (constantsDecrypter.Detected)
				initializeConstantsDecrypter();
			proxyCallFixer = new ProxyCallFixer(module, getFileData(), DeobfuscatedFile);
			proxyCallFixer.findDelegateCreator();
			if (!proxyCallFixer.Detected) {
				proxyCallFixerV1 = new ProxyCallFixerV1(module);
				proxyCallFixerV1.findDelegateCreator();
			}
			antiDebugger = new AntiDebugger(module);
			antiDebugger.find();
			antiDumping = new AntiDumping(module);
			antiDumping.find(DeobfuscatedFile);
			stringDecrypter = new StringDecrypter(module);
			stringDecrypter.find(DeobfuscatedFile);
			initializeStringDecrypter();
			unpacker = new Unpacker(module);
			unpacker.find(DeobfuscatedFile, this);
		}

		byte[] getFileData() {
			if (ModuleBytes != null)
				return ModuleBytes;
			return ModuleBytes = DeobUtils.readModule(module);
		}

		[Flags]
		enum DecryptState {
			CanDecryptMethods = 1,
			CanUnpack = 2,
		}
		DecryptState decryptState = DecryptState.CanDecryptMethods | DecryptState.CanUnpack;
		public override bool getDecryptedModule(int count, ref byte[] newFileData, ref DumpedMethods dumpedMethods) {
			byte[] fileData = getFileData();
			var peImage = new PeImage(fileData);

			if ((decryptState & DecryptState.CanDecryptMethods) != 0) {
				if (jitMethodsDecrypter != null && jitMethodsDecrypter.Detected) {
					jitMethodsDecrypter.initialize();
					if (!jitMethodsDecrypter.decrypt(peImage, fileData, ref dumpedMethods))
						return false;

					decryptState &= ~DecryptState.CanDecryptMethods;
					newFileData = fileData;
					ModuleBytes = newFileData;
					return true;
				}

				if (memoryMethodsDecrypter != null && memoryMethodsDecrypter.Detected) {
					memoryMethodsDecrypter.initialize();
					if (!memoryMethodsDecrypter.decrypt(peImage, fileData, ref dumpedMethods))
						return false;

					decryptState &= ~DecryptState.CanDecryptMethods;
					newFileData = fileData;
					ModuleBytes = newFileData;
					return true;
				}
			}

			if ((decryptState & DecryptState.CanUnpack) != 0) {
				if (unpacker != null && unpacker.Detected) {
					decryptState &= ~DecryptState.CanUnpack;
					decryptState |= DecryptState.CanDecryptMethods;
					newFileData = unpacker.unpack();
					ModuleBytes = newFileData;
					return true;
				}
			}

			return false;
		}

		public override IDeobfuscator moduleReloaded(ModuleDefinition module) {
			var newOne = new Deobfuscator(options);
			DeobfuscatedFile.setDeobfuscator(newOne);
			newOne.decryptState = decryptState;
			newOne.DeobfuscatedFile = DeobfuscatedFile;
			newOne.ModuleBytes = ModuleBytes;
			newOne.setModule(module);
			newOne.jitMethodsDecrypter = new JitMethodsDecrypter(module, jitMethodsDecrypter);
			newOne.memoryMethodsDecrypter = new MemoryMethodsDecrypter(module, memoryMethodsDecrypter);
			newOne.initTheRest();
			return newOne;
		}

		public override void deobfuscateBegin() {
			base.deobfuscateBegin();

			removeObfuscatorAttribute();
			initializeConstantsDecrypter();
			initializeStringDecrypter();

			if (jitMethodsDecrypter != null) {
				addModuleCctorInitCallToBeRemoved(jitMethodsDecrypter.InitMethod);
				addTypeToBeRemoved(jitMethodsDecrypter.Type, "Method decrypter (JIT) type");
			}

			if (memoryMethodsDecrypter != null) {
				addModuleCctorInitCallToBeRemoved(memoryMethodsDecrypter.InitMethod);
				addTypeToBeRemoved(memoryMethodsDecrypter.Type, "Method decrypter (memory) type");
			}

			if (options.RemoveAntiDebug) {
				addModuleCctorInitCallToBeRemoved(antiDebugger.InitMethod);
				addTypeToBeRemoved(antiDebugger.Type, "Anti debugger type");
			}

			if (options.RemoveAntiDump) {
				addModuleCctorInitCallToBeRemoved(antiDumping.InitMethod);
				addTypeToBeRemoved(antiDumping.Type, "Anti dumping type");
			}

			if (proxyCallFixer != null)
				proxyCallFixer.find();
			if (proxyCallFixerV1 != null)
				proxyCallFixerV1.find();

			startedDeobfuscating = true;
		}

		bool hasInitializedStringDecrypter = false;
		void initializeStringDecrypter() {
			if (hasInitializedStringDecrypter)
				return;
			hasInitializedStringDecrypter = true;

			if (stringDecrypter != null && stringDecrypter.Detected) {
				decryptResources();
				stringDecrypter.initialize();
				staticStringInliner.add(stringDecrypter.Method, (method, gim, args) => stringDecrypter.decrypt(staticStringInliner.Method, (int)args[0]));
			}
		}

		bool hasInitializedConstantsDecrypter = false;
		void initializeConstantsDecrypter() {
			if (hasInitializedConstantsDecrypter)
				return;
			hasInitializedConstantsDecrypter = true;

			decryptResources();
			constantsDecrypter.initialize();
			int32ValueInliner = new Int32ValueInliner();
			int64ValueInliner = new Int64ValueInliner();
			singleValueInliner = new SingleValueInliner();
			doubleValueInliner = new DoubleValueInliner();
			foreach (var info in constantsDecrypter.Decrypters) {
				staticStringInliner.add(info.method, (method, gim, args) => constantsDecrypter.decryptString(method, gim, (uint)args[0], (ulong)args[1]));
				int32ValueInliner.add(info.method, (method, gim, args) => constantsDecrypter.decryptInt32(method, gim, (uint)args[0], (ulong)args[1]));
				int64ValueInliner.add(info.method, (method, gim, args) => constantsDecrypter.decryptInt64(method, gim, (uint)args[0], (ulong)args[1]));
				singleValueInliner.add(info.method, (method, gim, args) => constantsDecrypter.decryptSingle(method, gim, (uint)args[0], (ulong)args[1]));
				doubleValueInliner.add(info.method, (method, gim, args) => constantsDecrypter.decryptDouble(method, gim, (uint)args[0], (ulong)args[1]));
			}
			DeobfuscatedFile.stringDecryptersAdded();
			addTypesToBeRemoved(constantsDecrypter.Types, "Constants decrypter type");
			addFieldsToBeRemoved(constantsDecrypter.Fields, "Constants decrypter field");
			addMethodToBeRemoved(constantsDecrypter.NativeMethod, "Constants decrypter native method");
			addResourceToBeRemoved(constantsDecrypter.Resource, "Encrypted constants");
		}

		void decryptResources() {
			var rsrc = resourceDecrypter.mergeResources();
			if (rsrc == null)
				return;
			addResourceToBeRemoved(rsrc, "Encrypted resources");
			addMethodToBeRemoved(resourceDecrypter.Handler, "Resource decrypter handler");
			addFieldsToBeRemoved(resourceDecrypter.Fields, "Resource decrypter field");
		}

		void removeObfuscatorAttribute() {
			foreach (var type in module.Types) {
				if (type.FullName == "ConfusedByAttribute")
					addAttributeToBeRemoved(type, "Obfuscator attribute");
			}
		}

		public override void deobfuscateMethodEnd(Blocks blocks) {
			if (proxyCallFixer != null)
				proxyCallFixer.deobfuscate(blocks);
			if (proxyCallFixerV1 != null)
				proxyCallFixerV1.deobfuscate(blocks);
			resourceDecrypter.deobfuscate(blocks);
			int32ValueInliner.decrypt(blocks);
			int64ValueInliner.decrypt(blocks);
			singleValueInliner.decrypt(blocks);
			doubleValueInliner.decrypt(blocks);
			base.deobfuscateMethodEnd(blocks);
		}

		public override void deobfuscateEnd() {
			if (proxyCallFixer != null)
				removeProxyDelegates(proxyCallFixer);
			if (proxyCallFixerV1 != null) {
				if (removeProxyDelegates(proxyCallFixerV1))
					addFieldsToBeRemoved(proxyCallFixerV1.Fields, "Proxy delegate instance field");
				proxyCallFixerV1.cleanUp();
			}
			constantsDecrypter.cleanUp();

			if (CanRemoveStringDecrypterType) {
				if (stringDecrypter != null) {
					addMethodToBeRemoved(stringDecrypter.Method, "String decrypter method");
					addResourceToBeRemoved(stringDecrypter.Resource, "Encrypted strings");
				}
			}

			base.deobfuscateEnd();
		}

		public override IEnumerable<int> getStringDecrypterMethods() {
			var list = new List<int>();
			return list;
		}
	}
}
