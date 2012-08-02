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
		public const string THE_TYPE = "cr";
		BoolOption removeAntiDebug;
		BoolOption removeAntiDump;
		BoolOption decryptMainAsm;

		public DeobfuscatorInfo()
			: base() {
			removeAntiDebug = new BoolOption(null, makeArgName("antidb"), "Remove anti debug code", true);
			removeAntiDump = new BoolOption(null, makeArgName("antidump"), "Remove anti dump code", true);
			decryptMainAsm = new BoolOption(null, makeArgName("decrypt-main"), "Decrypt main embedded assembly", true);
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
				DecryptMainAsm = decryptMainAsm.get(),
			});
		}

		protected override IEnumerable<Option> getOptionsInternal() {
			return new List<Option>() {
				removeAntiDebug,
				removeAntiDump,
				decryptMainAsm,
			};
		}
	}

	class Deobfuscator : DeobfuscatorBase {
		Options options;
		string obfuscatorName = DeobfuscatorInfo.THE_NAME;

		List<EmbeddedAssemblyInfo> embeddedAssemblyInfos = new List<EmbeddedAssemblyInfo>();
		JitMethodsDecrypter jitMethodsDecrypter;
		MemoryMethodsDecrypter memoryMethodsDecrypter;
		ProxyCallFixer proxyCallFixer;
		ProxyCallFixerV1 proxyCallFixerV1;
		AntiDebugger antiDebugger;
		AntiDumping antiDumping;
		ResourceDecrypter resourceDecrypter;
		ConstantsDecrypter constantsDecrypter;
		ConstantsDecrypterV15 constantsDecrypterV15;
		Int32ValueInliner int32ValueInliner;
		Int64ValueInliner int64ValueInliner;
		SingleValueInliner singleValueInliner;
		DoubleValueInliner doubleValueInliner;
		StringDecrypter stringDecrypter;
		Unpacker unpacker;
		EmbeddedAssemblyInfo mainAsmInfo;

		bool startedDeobfuscating = false;

		internal class Options : OptionsBase {
			public bool RemoveAntiDebug { get; set; }
			public bool RemoveAntiDump { get; set; }
			public bool DecryptMainAsm { get; set; }
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
					toInt32(constantsDecrypterV15 != null ? constantsDecrypterV15.Detected : false) +
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
			constantsDecrypterV15 = new ConstantsDecrypterV15(module, DeobfuscatedFile);
			if (!constantsDecrypter.Detected)
				constantsDecrypterV15.find();
			if (constantsDecrypter.Detected)
				initializeConstantsDecrypter();
			else if (constantsDecrypterV15.Detected)
				initializeConstantsDecrypter15();
			proxyCallFixer = new ProxyCallFixer(module, getFileData(), DeobfuscatedFile);
			proxyCallFixer.findDelegateCreator();
			if (!proxyCallFixer.Detected) {
				proxyCallFixerV1 = new ProxyCallFixerV1(module);
				proxyCallFixerV1.findDelegateCreator(DeobfuscatedFile);
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
				bool decrypted = false;
				if (jitMethodsDecrypter != null && jitMethodsDecrypter.Detected) {
					jitMethodsDecrypter.initialize();
					if (!jitMethodsDecrypter.decrypt(peImage, fileData, ref dumpedMethods))
						return false;
					decrypted = true;
				}
				else if (memoryMethodsDecrypter != null && memoryMethodsDecrypter.Detected) {
					memoryMethodsDecrypter.initialize();
					if (!memoryMethodsDecrypter.decrypt(peImage, fileData))
						return false;
					decrypted = true;
				}

				if (decrypted) {
					decryptState &= ~DecryptState.CanDecryptMethods;
					decryptState |= DecryptState.CanUnpack;
					newFileData = fileData;
					ModuleBytes = newFileData;
					return true;
				}
			}

			if ((decryptState & DecryptState.CanUnpack) != 0) {
				if (unpacker != null && unpacker.Detected) {
					if (options.DecryptMainAsm) {
						decryptState |= DecryptState.CanDecryptMethods | DecryptState.CanUnpack;
						newFileData = unpacker.unpackMainAssembly().data;
						embeddedAssemblyInfos.AddRange(unpacker.getEmbeddedAssemblyInfos());
						ModuleBytes = newFileData;
						return true;
					}
					else {
						decryptState &= ~DecryptState.CanUnpack;
						mainAsmInfo = unpacker.unpackMainAssembly();
						embeddedAssemblyInfos.AddRange(unpacker.getEmbeddedAssemblyInfos());
						return false;
					}
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
			newOne.embeddedAssemblyInfos.AddRange(embeddedAssemblyInfos);
			newOne.setModule(module);
			newOne.jitMethodsDecrypter = new JitMethodsDecrypter(module, DeobfuscatedFile, jitMethodsDecrypter);
			if ((newOne.decryptState & DecryptState.CanDecryptMethods) != 0) {
				try {
					newOne.jitMethodsDecrypter.find();
				}
				catch {
				}
				if (newOne.jitMethodsDecrypter.Detected)
					return newOne;
			}
			newOne.memoryMethodsDecrypter = new MemoryMethodsDecrypter(module, DeobfuscatedFile, memoryMethodsDecrypter);
			if ((newOne.decryptState & DecryptState.CanDecryptMethods) != 0) {
				newOne.memoryMethodsDecrypter.find();
				if (newOne.memoryMethodsDecrypter.Detected)
					return newOne;
			}
			newOne.initTheRest();
			return newOne;
		}

		public override void deobfuscateBegin() {
			base.deobfuscateBegin();

			removeObfuscatorAttribute();
			initializeConstantsDecrypter();
			initializeConstantsDecrypter15();
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
				if (antiDebugger.Type == DotNetUtils.getModuleType(module))
					addMethodToBeRemoved(antiDebugger.InitMethod, "Anti debugger method");
			}

			if (options.RemoveAntiDump) {
				addModuleCctorInitCallToBeRemoved(antiDumping.InitMethod);
				addTypeToBeRemoved(antiDumping.Type, "Anti dumping type");
			}

			if (proxyCallFixer != null)
				proxyCallFixer.find();
			if (proxyCallFixerV1 != null)
				proxyCallFixerV1.find();

			removeInvalidResources();
			dumpEmbeddedAssemblies();

			startedDeobfuscating = true;
		}

		void dumpEmbeddedAssemblies() {
			if (mainAsmInfo != null) {
				DeobfuscatedFile.createAssemblyFile(mainAsmInfo.data, mainAsmInfo.asmSimpleName + "_real", mainAsmInfo.extension);
				addResourceToBeRemoved(mainAsmInfo.resource, string.Format("Embedded assembly: {0}", mainAsmInfo.asmFullName));
			}
			foreach (var info in embeddedAssemblyInfos) {
				if (info.asmFullName != module.Assembly.Name.FullName)
					DeobfuscatedFile.createAssemblyFile(info.data, info.asmSimpleName, info.extension);
				addResourceToBeRemoved(info.resource, string.Format("Embedded assembly: {0}", info.asmFullName));
			}
			embeddedAssemblyInfos.Clear();
		}

		void removeInvalidResources() {
			foreach (var rsrc in module.Resources) {
				var resource = rsrc as EmbeddedResource;
				if (resource == null)
					continue;
				if (resource.Offset != 0xFFFFFFFF)
					continue;
				addResourceToBeRemoved(resource, "Invalid resource");
			}
		}

		bool hasInitializedStringDecrypter = false;
		void initializeStringDecrypter() {
			if (hasInitializedStringDecrypter || (stringDecrypter== null || !stringDecrypter.Detected))
				return;
			hasInitializedStringDecrypter = true;

			decryptResources();
			stringDecrypter.initialize();
			staticStringInliner.add(stringDecrypter.Method, (method, gim, args) => stringDecrypter.decrypt(staticStringInliner.Method, (int)args[0]));
		}

		bool hasInitializedConstantsDecrypter = false;
		void initializeConstantsDecrypter() {
			if (hasInitializedConstantsDecrypter || (constantsDecrypter == null || !constantsDecrypter.Detected))
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

		bool hasInitializedConstantsDecrypter15 = false;
		void initializeConstantsDecrypter15() {
			if (hasInitializedConstantsDecrypter15 || (constantsDecrypterV15 == null || !constantsDecrypterV15.Detected))
				return;
			hasInitializedConstantsDecrypter15 = true;

			decryptResources();
			constantsDecrypterV15.initialize();
			int32ValueInliner = new Int32ValueInliner();
			int64ValueInliner = new Int64ValueInliner();
			singleValueInliner = new SingleValueInliner();
			doubleValueInliner = new DoubleValueInliner();
			staticStringInliner.add(constantsDecrypterV15.Method, (method, gim, args) => constantsDecrypterV15.decryptString(staticStringInliner.Method, (uint)args[0]));
			int32ValueInliner.add(constantsDecrypterV15.Method, (method, gim, args) => constantsDecrypterV15.decryptInt32(int32ValueInliner.Method, (uint)args[0]));
			int64ValueInliner.add(constantsDecrypterV15.Method, (method, gim, args) => constantsDecrypterV15.decryptInt64(int64ValueInliner.Method, (uint)args[0]));
			singleValueInliner.add(constantsDecrypterV15.Method, (method, gim, args) => constantsDecrypterV15.decryptSingle(singleValueInliner.Method, (uint)args[0]));
			doubleValueInliner.add(constantsDecrypterV15.Method, (method, gim, args) => constantsDecrypterV15.decryptDouble(doubleValueInliner.Method, (uint)args[0]));
			int32ValueInliner.RemoveUnbox = true;
			int64ValueInliner.RemoveUnbox = true;
			singleValueInliner.RemoveUnbox = true;
			doubleValueInliner.RemoveUnbox = true;
			DeobfuscatedFile.stringDecryptersAdded();
			addMethodToBeRemoved(constantsDecrypterV15.Method, "Constants decrypter method");
			addResourceToBeRemoved(constantsDecrypterV15.Resource, "Encrypted constants");
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
			unpacker.deobfuscate(blocks);
			if (int32ValueInliner != null) {
				int32ValueInliner.decrypt(blocks);
				int64ValueInliner.decrypt(blocks);
				singleValueInliner.decrypt(blocks);
				doubleValueInliner.decrypt(blocks);
			}
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
