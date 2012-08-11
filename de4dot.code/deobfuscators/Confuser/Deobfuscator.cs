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
		AntiDebugger antiDebugger;
		AntiDumping antiDumping;
		ResourceDecrypter resourceDecrypter;
		ConstantsDecrypterV18 constantsDecrypterV18;
		ConstantsDecrypterV17 constantsDecrypterV17;
		ConstantsDecrypterV15 constantsDecrypterV15;
		Int32ValueInliner int32ValueInliner;
		Int64ValueInliner int64ValueInliner;
		SingleValueInliner singleValueInliner;
		DoubleValueInliner doubleValueInliner;
		StringDecrypter stringDecrypter;
		Unpacker unpacker;
		EmbeddedAssemblyInfo mainAsmInfo;
		RealAssemblyInfo realAssemblyInfo;

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
					toInt32(antiDebugger != null ? antiDebugger.Detected : false) +
					toInt32(antiDumping != null ? antiDumping.Detected : false) +
					toInt32(resourceDecrypter != null ? resourceDecrypter.Detected : false) +
					toInt32(constantsDecrypterV18 != null ? constantsDecrypterV18.Detected : false) +
					toInt32(constantsDecrypterV15 != null ? constantsDecrypterV15.Detected : false) +
					toInt32(constantsDecrypterV17 != null ? constantsDecrypterV17.Detected : false) +
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
			if (jitMethodsDecrypter.Detected) {
				initializeObfuscatorName();
				return;
			}
			memoryMethodsDecrypter = new MemoryMethodsDecrypter(module, DeobfuscatedFile);
			memoryMethodsDecrypter.find();
			if (memoryMethodsDecrypter.Detected) {
				initializeObfuscatorName();
				return;
			}
			initTheRest(null);
		}

		void initTheRest(Deobfuscator oldOne) {
			resourceDecrypter = new ResourceDecrypter(module, DeobfuscatedFile);
			resourceDecrypter.find();

			constantsDecrypterV18 = new ConstantsDecrypterV18(module, getFileData(), DeobfuscatedFile);
			constantsDecrypterV17 = new ConstantsDecrypterV17(module, getFileData(), DeobfuscatedFile);
			constantsDecrypterV15 = new ConstantsDecrypterV15(module, getFileData(), DeobfuscatedFile);
			do {
				constantsDecrypterV18.find();
				if (constantsDecrypterV18.Detected) {
					initializeConstantsDecrypterV18();
					break;
				}
				constantsDecrypterV17.find();
				if (constantsDecrypterV17.Detected) {
					initializeConstantsDecrypterV17();
					break;
				}
				constantsDecrypterV15.find();
				if (constantsDecrypterV15.Detected) {
					initializeConstantsDecrypterV15();
					break;
				}
			} while (false);

			proxyCallFixer = new ProxyCallFixer(module, getFileData());
			proxyCallFixer.findDelegateCreator(DeobfuscatedFile);
			antiDebugger = new AntiDebugger(module);
			antiDebugger.find();
			antiDumping = new AntiDumping(module);
			antiDumping.find(DeobfuscatedFile);
			stringDecrypter = new StringDecrypter(module);
			stringDecrypter.find(DeobfuscatedFile);
			initializeStringDecrypter();
			unpacker = new Unpacker(module, oldOne == null ? null : oldOne.unpacker);
			unpacker.find(DeobfuscatedFile, this);
			initializeObfuscatorName();
		}

		void initializeObfuscatorName() {
			var versionString = getVersionString();
			if (string.IsNullOrEmpty(versionString))
				obfuscatorName = DeobfuscatorInfo.THE_NAME;
			else
				obfuscatorName = string.Format("{0} {1}", DeobfuscatorInfo.THE_NAME, versionString);
		}

		string getVersionString() {
			var versionProviders = new IVersionProvider[] {
				jitMethodsDecrypter,
				memoryMethodsDecrypter,
				proxyCallFixer,
				antiDebugger,
				antiDumping,
				resourceDecrypter,
				constantsDecrypterV18,
				constantsDecrypterV17,
				constantsDecrypterV15,
				stringDecrypter,
				unpacker,
			};

			var vd = new VersionDetector();
			foreach (var versionProvider in versionProviders) {
				if (versionProvider == null)
					continue;
				int minRev, maxRev;
				if (versionProvider.getRevisionRange(out minRev, out maxRev)) {
					if (maxRev == int.MaxValue)
						Log.v("r{0}-latest : {1}", minRev, versionProvider.GetType().Name);
					else
						Log.v("r{0}-r{1} : {2}", minRev, maxRev, versionProvider.GetType().Name);
					vd.addRevs(minRev, maxRev);
				}
			}
			return vd.getVersionString();
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
		bool hasUnpacked = false;
		public override bool getDecryptedModule(int count, ref byte[] newFileData, ref DumpedMethods dumpedMethods) {
			hasUnpacked = false;
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
						var mainInfo = unpacker.unpackMainAssembly(true);
						newFileData = mainInfo.data;
						realAssemblyInfo = mainInfo.realAssemblyInfo;
						embeddedAssemblyInfos.AddRange(unpacker.getEmbeddedAssemblyInfos());
						ModuleBytes = newFileData;
						hasUnpacked = true;
						return true;
					}
					else {
						decryptState &= ~DecryptState.CanUnpack;
						mainAsmInfo = unpacker.unpackMainAssembly(false);
						embeddedAssemblyInfos.AddRange(unpacker.getEmbeddedAssemblyInfos());
						return false;
					}
				}
			}

			return false;
		}

		public override IDeobfuscator moduleReloaded(ModuleDefinition module) {
			if (module.Assembly != null)
				realAssemblyInfo = null;
			if (realAssemblyInfo != null) {
				module.Assembly = realAssemblyInfo.realAssembly;
				module.Assembly.MainModule = module;
				if (realAssemblyInfo.entryPointToken != 0)
					module.EntryPoint = (MethodDefinition)module.LookupToken((int)realAssemblyInfo.entryPointToken);
				module.Kind = realAssemblyInfo.kind;
				module.Name = realAssemblyInfo.moduleName;
			}

			var newOne = new Deobfuscator(options);
			DeobfuscatedFile.setDeobfuscator(newOne);
			newOne.realAssemblyInfo = realAssemblyInfo;
			newOne.decryptState = decryptState;
			newOne.DeobfuscatedFile = DeobfuscatedFile;
			newOne.ModuleBytes = ModuleBytes;
			newOne.embeddedAssemblyInfos.AddRange(embeddedAssemblyInfos);
			newOne.setModule(module);
			newOne.jitMethodsDecrypter = hasUnpacked ? new JitMethodsDecrypter(module, DeobfuscatedFile) :
						new JitMethodsDecrypter(module, DeobfuscatedFile, jitMethodsDecrypter);
			if ((newOne.decryptState & DecryptState.CanDecryptMethods) != 0) {
				try {
					newOne.jitMethodsDecrypter.find();
				}
				catch {
				}
				if (newOne.jitMethodsDecrypter.Detected)
					return newOne;
			}
			newOne.memoryMethodsDecrypter = hasUnpacked ? new MemoryMethodsDecrypter(module, DeobfuscatedFile) :
						new MemoryMethodsDecrypter(module, DeobfuscatedFile, memoryMethodsDecrypter);
			if ((newOne.decryptState & DecryptState.CanDecryptMethods) != 0) {
				newOne.memoryMethodsDecrypter.find();
				if (newOne.memoryMethodsDecrypter.Detected)
					return newOne;
			}
			newOne.initTheRest(this);
			return newOne;
		}

		public override void deobfuscateBegin() {
			base.deobfuscateBegin();

			Log.v("Detected {0}", obfuscatorName);

			removeObfuscatorAttribute();
			initializeConstantsDecrypterV18();
			initializeConstantsDecrypterV17();
			initializeConstantsDecrypterV15();
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

			removeInvalidResources();
			removeInvalidAssemblyReferences();
			dumpEmbeddedAssemblies();

			startedDeobfuscating = true;
		}

		void dumpEmbeddedAssemblies() {
			if (mainAsmInfo != null) {
				var asm = module.Assembly;
				var name = asm == null ? module.Name : asm.Name.Name;
				DeobfuscatedFile.createAssemblyFile(mainAsmInfo.data, name + "_real", mainAsmInfo.extension);
				addResourceToBeRemoved(mainAsmInfo.resource, string.Format("Embedded assembly: {0}", mainAsmInfo.asmFullName));
			}
			foreach (var info in embeddedAssemblyInfos) {
				if (module.Assembly == null || info.asmFullName != module.Assembly.Name.FullName)
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

		void removeInvalidAssemblyReferences() {
			// Confuser 1.7 r73764 adds an invalid assembly reference:
			//	version: 0.0.0.0
			//	attrs: SideBySideCompatible
			//	key: 0 (cecil sets pkt to zero length array)
			//	name: 0xFFFF
			//	culture: 0
			//	hash: 0xFFFF
			foreach (var asmRef in module.AssemblyReferences) {
				if (asmRef.Attributes != AssemblyAttributes.SideBySideCompatible)
					continue;
				if (asmRef.Version != null && asmRef.Version != new Version(0, 0, 0, 0))
					continue;
				if (asmRef.PublicKeyToken == null || asmRef.PublicKeyToken.Length != 0)
					continue;
				if (asmRef.Culture.Length != 0)
					continue;

				addAssemblyReferenceToBeRemoved(asmRef, "Invalid assembly reference");
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
		void initializeConstantsDecrypterV18() {
			if (hasInitializedConstantsDecrypter || (constantsDecrypterV18 == null || !constantsDecrypterV18.Detected))
				return;
			hasInitializedConstantsDecrypter = true;

			decryptResources();
			constantsDecrypterV18.initialize();
			int32ValueInliner = new Int32ValueInliner();
			int64ValueInliner = new Int64ValueInliner();
			singleValueInliner = new SingleValueInliner();
			doubleValueInliner = new DoubleValueInliner();
			foreach (var info in constantsDecrypterV18.Decrypters) {
				staticStringInliner.add(info.method, (method, gim, args) => constantsDecrypterV18.decryptString(method, gim, (uint)args[0], (ulong)args[1]));
				int32ValueInliner.add(info.method, (method, gim, args) => constantsDecrypterV18.decryptInt32(method, gim, (uint)args[0], (ulong)args[1]));
				int64ValueInliner.add(info.method, (method, gim, args) => constantsDecrypterV18.decryptInt64(method, gim, (uint)args[0], (ulong)args[1]));
				singleValueInliner.add(info.method, (method, gim, args) => constantsDecrypterV18.decryptSingle(method, gim, (uint)args[0], (ulong)args[1]));
				doubleValueInliner.add(info.method, (method, gim, args) => constantsDecrypterV18.decryptDouble(method, gim, (uint)args[0], (ulong)args[1]));
			}
			DeobfuscatedFile.stringDecryptersAdded();
			addTypesToBeRemoved(constantsDecrypterV18.Types, "Constants decrypter type");
			addFieldsToBeRemoved(constantsDecrypterV18.Fields, "Constants decrypter field");
			addMethodToBeRemoved(constantsDecrypterV18.NativeMethod, "Constants decrypter native method");
			addResourceToBeRemoved(constantsDecrypterV18.Resource, "Encrypted constants");
		}

		bool hasInitializedConstantsDecrypter15 = false;
		void initializeConstantsDecrypterV15() {
			initialize(constantsDecrypterV15, ref hasInitializedConstantsDecrypter15);
		}

		bool hasInitializedConstantsDecrypter17 = false;
		void initializeConstantsDecrypterV17() {
			initialize(constantsDecrypterV17, ref hasInitializedConstantsDecrypter17);
		}

		void initialize(ConstantsDecrypterBase constDecrypter, ref bool hasInitialized) {
			if (hasInitialized || (constDecrypter == null || !constDecrypter.Detected))
				return;
			hasInitializedConstantsDecrypter15 = true;

			decryptResources();
			constDecrypter.initialize();
			int32ValueInliner = new Int32ValueInliner();
			int64ValueInliner = new Int64ValueInliner();
			singleValueInliner = new SingleValueInliner();
			doubleValueInliner = new DoubleValueInliner();
			foreach (var info in constDecrypter.DecrypterInfos) {
				staticStringInliner.add(info.decryptMethod, (method, gim, args) => constDecrypter.decryptString(staticStringInliner.Method, method, args));
				int32ValueInliner.add(info.decryptMethod, (method, gim, args) => constDecrypter.decryptInt32(int32ValueInliner.Method, method, args));
				int64ValueInliner.add(info.decryptMethod, (method, gim, args) => constDecrypter.decryptInt64(int64ValueInliner.Method, method, args));
				singleValueInliner.add(info.decryptMethod, (method, gim, args) => constDecrypter.decryptSingle(singleValueInliner.Method, method, args));
				doubleValueInliner.add(info.decryptMethod, (method, gim, args) => constDecrypter.decryptDouble(doubleValueInliner.Method, method, args));
			}
			int32ValueInliner.RemoveUnbox = true;
			int64ValueInliner.RemoveUnbox = true;
			singleValueInliner.RemoveUnbox = true;
			doubleValueInliner.RemoveUnbox = true;
			DeobfuscatedFile.stringDecryptersAdded();
			addFieldsToBeRemoved(constDecrypter.Fields, "Constants decrypter field");
			var moduleType = DotNetUtils.getModuleType(module);
			foreach (var info in constDecrypter.DecrypterInfos) {
				if (info.decryptMethod.DeclaringType == moduleType)
					addMethodToBeRemoved(info.decryptMethod, "Constants decrypter method");
				else
					addTypeToBeRemoved(info.decryptMethod.DeclaringType, "Constants decrypter type");
			}
			addMethodToBeRemoved(constDecrypter.NativeMethod, "Constants decrypter native method");
			addResourceToBeRemoved(constDecrypter.Resource, "Encrypted constants");
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
			if (proxyCallFixer != null) {
				if (removeProxyDelegates(proxyCallFixer))
					addFieldsToBeRemoved(proxyCallFixer.Fields, "Proxy delegate instance field");
				proxyCallFixer.cleanUp();
			}
			constantsDecrypterV18.cleanUp();

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
