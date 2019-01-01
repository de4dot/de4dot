/*
    Copyright (C) 2011-2015 de4dot@gmail.com

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
using dnlib.DotNet;
using de4dot.blocks;
using de4dot.blocks.cflow;

namespace de4dot.code.deobfuscators.Confuser {
	public class DeobfuscatorInfo : DeobfuscatorInfoBase {
		public const string THE_NAME = "Confuser";
		public const string THE_TYPE = "cr";
		const string DEFAULT_REGEX = DeobfuscatorBase.DEFAULT_VALID_NAME_REGEX;
		BoolOption removeAntiDebug;
		BoolOption removeAntiDump;
		BoolOption decryptMainAsm;

		public DeobfuscatorInfo()
			: base(DEFAULT_REGEX) {
			removeAntiDebug = new BoolOption(null, MakeArgName("antidb"), "Remove anti debug code", true);
			removeAntiDump = new BoolOption(null, MakeArgName("antidump"), "Remove anti dump code", true);
			decryptMainAsm = new BoolOption(null, MakeArgName("decrypt-main"), "Decrypt main embedded assembly", true);
		}

		public override string Name => THE_NAME;
		public override string Type => THE_TYPE;

		public override IDeobfuscator CreateDeobfuscator() =>
			new Deobfuscator(new Deobfuscator.Options {
				ValidNameRegex = validNameRegex.Get(),
				RemoveAntiDebug = removeAntiDebug.Get(),
				RemoveAntiDump = removeAntiDump.Get(),
				DecryptMainAsm = decryptMainAsm.Get(),
			});

		protected override IEnumerable<Option> GetOptionsInternal() =>
			new List<Option>() {
				removeAntiDebug,
				removeAntiDump,
				decryptMainAsm,
			};
	}

	class Deobfuscator : DeobfuscatorBase, IStringDecrypter {
		Options options;
		string obfuscatorName = DeobfuscatorInfo.THE_NAME;
		Version approxVersion;

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

		public override string Type => DeobfuscatorInfo.THE_TYPE;
		public override string TypeLong => DeobfuscatorInfo.THE_NAME;
		public override string Name => obfuscatorName;

		public override IEnumerable<IBlocksDeobfuscator> BlocksDeobfuscators {
			get {
				var list = new List<IBlocksDeobfuscator>();
				list.Add(new ConstantsFolder { ExecuteIfNotModified = true });

				// Add this one last so all cflow is deobfuscated whenever it executes
				if (!startedDeobfuscating && int32ValueInliner != null)
					list.Add(new ConstantsInliner(int32ValueInliner, int64ValueInliner, singleValueInliner, doubleValueInliner) { ExecuteIfNotModified = true });

				return list;
			}
		}

		public Deobfuscator(Options options)
			: base(options) {
			this.options = options;
			StringFeatures = StringFeatures.AllowStaticDecryption | StringFeatures.AllowDynamicDecryption;
		}

		protected override int DetectInternal() {
			int val = 0;

			int sum = ToInt32(jitMethodsDecrypter != null ? jitMethodsDecrypter.Detected : false) +
					ToInt32(memoryMethodsDecrypter != null ? memoryMethodsDecrypter.Detected : false) +
					ToInt32(proxyCallFixer != null ? proxyCallFixer.Detected : false) +
					ToInt32(antiDebugger != null ? antiDebugger.Detected : false) +
					ToInt32(antiDumping != null ? antiDumping.Detected : false) +
					ToInt32(resourceDecrypter != null ? resourceDecrypter.Detected : false) +
					ToInt32(constantsDecrypterV18 != null ? constantsDecrypterV18.Detected : false) +
					ToInt32(constantsDecrypterV15 != null ? constantsDecrypterV15.Detected : false) +
					ToInt32(constantsDecrypterV17 != null ? constantsDecrypterV17.Detected : false) +
					ToInt32(stringDecrypter != null ? stringDecrypter.Detected : false) +
					ToInt32(unpacker != null ? unpacker.Detected : false);
			if (sum > 0)
				val += 100 + 10 * (sum - 1);

			return val;
		}

		protected override void ScanForObfuscator() {
			RemoveObfuscatorAttribute();
			jitMethodsDecrypter = new JitMethodsDecrypter(module, DeobfuscatedFile);
			try {
				jitMethodsDecrypter.Find();
			}
			catch {
			}
			if (jitMethodsDecrypter.Detected) {
				InitializeObfuscatorName();
				return;
			}
			memoryMethodsDecrypter = new MemoryMethodsDecrypter(module, DeobfuscatedFile);
			memoryMethodsDecrypter.Find();
			if (memoryMethodsDecrypter.Detected) {
				InitializeObfuscatorName();
				return;
			}
			InitializeTheRest(null);
		}

		void InitializeTheRest(Deobfuscator oldOne) {
			resourceDecrypter = new ResourceDecrypter(module, DeobfuscatedFile);
			resourceDecrypter.Find();

			constantsDecrypterV18 = new ConstantsDecrypterV18(module, GetFileData(), DeobfuscatedFile);
			constantsDecrypterV17 = new ConstantsDecrypterV17(module, GetFileData(), DeobfuscatedFile);
			constantsDecrypterV15 = new ConstantsDecrypterV15(module, GetFileData(), DeobfuscatedFile);
			do {
				constantsDecrypterV18.Find();
				if (constantsDecrypterV18.Detected) {
					InitializeConstantsDecrypterV18();
					break;
				}
				constantsDecrypterV17.Find();
				if (constantsDecrypterV17.Detected) {
					InitializeConstantsDecrypterV17();
					break;
				}
				constantsDecrypterV15.Find();
				if (constantsDecrypterV15.Detected) {
					InitializeConstantsDecrypterV15();
					break;
				}
			} while (false);

			proxyCallFixer = new ProxyCallFixer(module, GetFileData());
			proxyCallFixer.FindDelegateCreator(DeobfuscatedFile);
			antiDebugger = new AntiDebugger(module);
			antiDebugger.Find();
			antiDumping = new AntiDumping(module);
			antiDumping.Find(DeobfuscatedFile);
			stringDecrypter = new StringDecrypter(module);
			stringDecrypter.Find(DeobfuscatedFile);
			InitializeStringDecrypter();
			unpacker = new Unpacker(module, oldOne?.unpacker);
			unpacker.Find(DeobfuscatedFile, this);
			InitializeObfuscatorName();
		}

		void InitializeObfuscatorName() {
			var versionString = GetVersionString();
			if (string.IsNullOrEmpty(versionString))
				obfuscatorName = DeobfuscatorInfo.THE_NAME;
			else
				obfuscatorName = $"{DeobfuscatorInfo.THE_NAME} {versionString}";
		}

		const bool useAttributeVersion = true;
		string GetVersionString() {
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
				if (versionProvider.GetRevisionRange(out int minRev, out int maxRev)) {
					if (maxRev == int.MaxValue)
						Logger.v("r{0}-latest : {1}", minRev, versionProvider.GetType().Name);
					else
						Logger.v("r{0}-r{1} : {2}", minRev, maxRev, versionProvider.GetType().Name);
					vd.AddRevs(minRev, maxRev);
				}
			}
			if (useAttributeVersion)
				vd.SetVersion(approxVersion);
			return vd.GetVersionString();
		}

		byte[] GetFileData() {
			if (ModuleBytes != null)
				return ModuleBytes;
			return ModuleBytes = DeobUtils.ReadModule(module);
		}

		[Flags]
		enum DecryptState {
			CanDecryptMethods = 1,
			CanUnpack = 2,
		}
		DecryptState decryptState = DecryptState.CanDecryptMethods | DecryptState.CanUnpack;
		bool hasUnpacked = false;
		public override bool GetDecryptedModule(int count, ref byte[] newFileData, ref DumpedMethods dumpedMethods) {
			hasUnpacked = false;
			byte[] fileData = GetFileData();

			using (var peImage = new MyPEImage(fileData)) {
				if ((decryptState & DecryptState.CanDecryptMethods) != 0) {
					bool decrypted = false;
					if (jitMethodsDecrypter != null && jitMethodsDecrypter.Detected) {
						jitMethodsDecrypter.Initialize();
						if (!jitMethodsDecrypter.Decrypt(peImage, fileData, ref dumpedMethods))
							return false;
						decrypted = true;
					}
					else if (memoryMethodsDecrypter != null && memoryMethodsDecrypter.Detected) {
						memoryMethodsDecrypter.Initialize();
						if (!memoryMethodsDecrypter.Decrypt(peImage, fileData))
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
			}

			if ((decryptState & DecryptState.CanUnpack) != 0) {
				if (unpacker != null && unpacker.Detected) {
					if (options.DecryptMainAsm) {
						decryptState |= DecryptState.CanDecryptMethods | DecryptState.CanUnpack;
						var mainInfo = unpacker.UnpackMainAssembly(true);
						newFileData = mainInfo.data;
						realAssemblyInfo = mainInfo.realAssemblyInfo;
						embeddedAssemblyInfos.AddRange(unpacker.GetEmbeddedAssemblyInfos());
						ModuleBytes = newFileData;
						hasUnpacked = true;
						return true;
					}
					else {
						decryptState &= ~DecryptState.CanUnpack;
						mainAsmInfo = unpacker.UnpackMainAssembly(false);
						embeddedAssemblyInfos.AddRange(unpacker.GetEmbeddedAssemblyInfos());
						return false;
					}
				}
			}

			return false;
		}

		public override IDeobfuscator ModuleReloaded(ModuleDefMD module) {
			if (module.Assembly != null)
				realAssemblyInfo = null;
			if (realAssemblyInfo != null) {
				realAssemblyInfo.realAssembly.Modules.Insert(0, module);
				if (realAssemblyInfo.entryPointToken != 0)
					module.EntryPoint = module.ResolveToken((int)realAssemblyInfo.entryPointToken) as MethodDef;
				module.Kind = realAssemblyInfo.kind;
				module.Name = new UTF8String(realAssemblyInfo.moduleName);
			}

			var newOne = new Deobfuscator(options);
			DeobfuscatedFile.SetDeobfuscator(newOne);
			newOne.realAssemblyInfo = realAssemblyInfo;
			newOne.decryptState = decryptState;
			newOne.DeobfuscatedFile = DeobfuscatedFile;
			newOne.ModuleBytes = ModuleBytes;
			newOne.embeddedAssemblyInfos.AddRange(embeddedAssemblyInfos);
			newOne.SetModule(module);
			newOne.RemoveObfuscatorAttribute();
			newOne.jitMethodsDecrypter = hasUnpacked ? new JitMethodsDecrypter(module, DeobfuscatedFile) :
						new JitMethodsDecrypter(module, DeobfuscatedFile, jitMethodsDecrypter);
			if ((newOne.decryptState & DecryptState.CanDecryptMethods) != 0) {
				try {
					newOne.jitMethodsDecrypter.Find();
				}
				catch {
				}
				if (newOne.jitMethodsDecrypter.Detected)
					return newOne;
			}
			newOne.memoryMethodsDecrypter = hasUnpacked ? new MemoryMethodsDecrypter(module, DeobfuscatedFile) :
						new MemoryMethodsDecrypter(module, DeobfuscatedFile, memoryMethodsDecrypter);
			if ((newOne.decryptState & DecryptState.CanDecryptMethods) != 0) {
				newOne.memoryMethodsDecrypter.Find();
				if (newOne.memoryMethodsDecrypter.Detected)
					return newOne;
			}
			newOne.InitializeTheRest(this);
			return newOne;
		}

		public override void DeobfuscateBegin() {
			base.DeobfuscateBegin();

			Logger.v("Detected {0}", obfuscatorName);

			InitializeConstantsDecrypterV18();
			InitializeConstantsDecrypterV17();
			InitializeConstantsDecrypterV15();
			InitializeStringDecrypter();

			if (jitMethodsDecrypter != null) {
				AddModuleCctorInitCallToBeRemoved(jitMethodsDecrypter.InitMethod);
				AddTypeToBeRemoved(jitMethodsDecrypter.Type, "Methods decrypter (JIT) type");
			}

			if (memoryMethodsDecrypter != null) {
				AddModuleCctorInitCallToBeRemoved(memoryMethodsDecrypter.InitMethod);
				AddTypeToBeRemoved(memoryMethodsDecrypter.Type, "Methods decrypter (memory) type");
			}

			if (options.RemoveAntiDebug && antiDebugger != null) {
				AddModuleCctorInitCallToBeRemoved(antiDebugger.InitMethod);
				AddTypeToBeRemoved(antiDebugger.Type, "Anti debugger type");
				if (antiDebugger.Type == DotNetUtils.GetModuleType(module))
					AddMethodToBeRemoved(antiDebugger.InitMethod, "Anti debugger method");
			}

			if (options.RemoveAntiDump && antiDumping != null) {
				AddModuleCctorInitCallToBeRemoved(antiDumping.InitMethod);
				AddTypeToBeRemoved(antiDumping.Type, "Anti dumping type");
			}

			if (proxyCallFixer != null)
				proxyCallFixer.Find();

			RemoveInvalidResources();
			DumpEmbeddedAssemblies();

			startedDeobfuscating = true;
		}

		void DumpEmbeddedAssemblies() {
			if (mainAsmInfo != null) {
				var asm = module.Assembly;
				var name = (asm == null ? module.Name : asm.Name).String;
				DeobfuscatedFile.CreateAssemblyFile(mainAsmInfo.data, name + "_real", mainAsmInfo.extension);
				AddResourceToBeRemoved(mainAsmInfo.resource, $"Embedded assembly: {mainAsmInfo.asmFullName}");
			}
			foreach (var info in embeddedAssemblyInfos) {
				if (module.Assembly == null || info.asmFullName != module.Assembly.FullName)
					DeobfuscatedFile.CreateAssemblyFile(info.data, info.asmSimpleName, info.extension);
				AddResourceToBeRemoved(info.resource, $"Embedded assembly: {info.asmFullName}");
			}
			embeddedAssemblyInfos.Clear();
		}

		void RemoveInvalidResources() {
			foreach (var rsrc in module.Resources) {
				var resource = rsrc as EmbeddedResource;
				if (resource == null)
					continue;
				if (resource.Offset != 0xFFFFFFFF)
					continue;
				AddResourceToBeRemoved(resource, "Invalid resource");
			}
		}

		bool hasInitializedStringDecrypter = false;
		void InitializeStringDecrypter() {
			if (hasInitializedStringDecrypter || (stringDecrypter== null || !stringDecrypter.Detected))
				return;
			hasInitializedStringDecrypter = true;

			DecryptResources();
			stringDecrypter.Initialize();
			staticStringInliner.Add(stringDecrypter.Method, (method, gim, args) => stringDecrypter.Decrypt(staticStringInliner.Method, (int)args[0]));
			DeobfuscatedFile.StringDecryptersAdded();
		}

		bool hasInitializedConstantsDecrypter = false;
		void InitializeConstantsDecrypterV18() {
			if (hasInitializedConstantsDecrypter || (constantsDecrypterV18 == null || !constantsDecrypterV18.Detected))
				return;
			hasInitializedConstantsDecrypter = true;

			DecryptResources();
			constantsDecrypterV18.Initialize();
			int32ValueInliner = new Int32ValueInliner();
			int64ValueInliner = new Int64ValueInliner();
			singleValueInliner = new SingleValueInliner();
			doubleValueInliner = new DoubleValueInliner();
			foreach (var info in constantsDecrypterV18.Decrypters) {
				staticStringInliner.Add(info.method, (method, gim, args) => constantsDecrypterV18.DecryptString(method, gim, (uint)args[0], (ulong)args[1]));
				int32ValueInliner.Add(info.method, (method, gim, args) => constantsDecrypterV18.DecryptInt32(method, gim, (uint)args[0], (ulong)args[1]));
				int64ValueInliner.Add(info.method, (method, gim, args) => constantsDecrypterV18.DecryptInt64(method, gim, (uint)args[0], (ulong)args[1]));
				singleValueInliner.Add(info.method, (method, gim, args) => constantsDecrypterV18.DecryptSingle(method, gim, (uint)args[0], (ulong)args[1]));
				doubleValueInliner.Add(info.method, (method, gim, args) => constantsDecrypterV18.DecryptDouble(method, gim, (uint)args[0], (ulong)args[1]));
			}
			DeobfuscatedFile.StringDecryptersAdded();
			AddTypesToBeRemoved(constantsDecrypterV18.Types, "Constants decrypter type");
			AddFieldsToBeRemoved(constantsDecrypterV18.Fields, "Constants decrypter field");
			AddMethodToBeRemoved(constantsDecrypterV18.NativeMethod, "Constants decrypter native method");
			AddTypeToBeRemoved(constantsDecrypterV18.LzmaType, "LZMA type");
			AddResourceToBeRemoved(constantsDecrypterV18.Resource, "Encrypted constants");
		}

		bool hasInitializedConstantsDecrypter15 = false;
		void InitializeConstantsDecrypterV15() => Initialize(constantsDecrypterV15, ref hasInitializedConstantsDecrypter15);

		bool hasInitializedConstantsDecrypter17 = false;
		void InitializeConstantsDecrypterV17() => Initialize(constantsDecrypterV17, ref hasInitializedConstantsDecrypter17);

		void Initialize(ConstantsDecrypterBase constDecrypter, ref bool hasInitialized) {
			if (hasInitialized || (constDecrypter == null || !constDecrypter.Detected))
				return;
			hasInitializedConstantsDecrypter15 = true;

			DecryptResources();
			constDecrypter.Initialize();
			int32ValueInliner = new Int32ValueInliner();
			int64ValueInliner = new Int64ValueInliner();
			singleValueInliner = new SingleValueInliner();
			doubleValueInliner = new DoubleValueInliner();
			foreach (var info in constDecrypter.DecrypterInfos) {
				staticStringInliner.Add(info.decryptMethod, (method, gim, args) => constDecrypter.DecryptString(staticStringInliner.Method, method, args));
				int32ValueInliner.Add(info.decryptMethod, (method, gim, args) => constDecrypter.DecryptInt32(int32ValueInliner.Method, method, args));
				int64ValueInliner.Add(info.decryptMethod, (method, gim, args) => constDecrypter.DecryptInt64(int64ValueInliner.Method, method, args));
				singleValueInliner.Add(info.decryptMethod, (method, gim, args) => constDecrypter.DecryptSingle(singleValueInliner.Method, method, args));
				doubleValueInliner.Add(info.decryptMethod, (method, gim, args) => constDecrypter.DecryptDouble(doubleValueInliner.Method, method, args));
			}
			int32ValueInliner.RemoveUnbox = true;
			int64ValueInliner.RemoveUnbox = true;
			singleValueInliner.RemoveUnbox = true;
			doubleValueInliner.RemoveUnbox = true;
			DeobfuscatedFile.StringDecryptersAdded();
			AddFieldsToBeRemoved(constDecrypter.Fields, "Constants decrypter field");
			var moduleType = DotNetUtils.GetModuleType(module);
			foreach (var info in constDecrypter.DecrypterInfos) {
				if (info.decryptMethod.DeclaringType == moduleType)
					AddMethodToBeRemoved(info.decryptMethod, "Constants decrypter method");
				else
					AddTypeToBeRemoved(info.decryptMethod.DeclaringType, "Constants decrypter type");
			}
			AddMethodToBeRemoved(constDecrypter.NativeMethod, "Constants decrypter native method");
			AddResourceToBeRemoved(constDecrypter.Resource, "Encrypted constants");
		}

		void DecryptResources() {
			var rsrc = resourceDecrypter.MergeResources();
			if (rsrc == null)
				return;
			AddResourceToBeRemoved(rsrc, "Encrypted resources");
			AddMethodToBeRemoved(resourceDecrypter.Handler, "Resource decrypter handler");
			AddFieldsToBeRemoved(resourceDecrypter.Fields, "Resource decrypter field");
			AddTypeToBeRemoved(resourceDecrypter.LzmaType, "LZMA type");
		}

		void RemoveObfuscatorAttribute() {
			foreach (var type in module.Types) {
				if (type.FullName == "ConfusedByAttribute") {
					SetConfuserVersion(type);
					AddAttributeToBeRemoved(type, "Obfuscator attribute");
					break;
				}
			}
		}

		void SetConfuserVersion(TypeDef type) {
			var s = DotNetUtils.GetCustomArgAsString(GetModuleAttribute(type) ?? GetAssemblyAttribute(type), 0);
			if (s == null)
				return;
			var val = System.Text.RegularExpressions.Regex.Match(s, @"^Confuser v(\d+)\.(\d+)\.(\d+)\.(\d+)$");
			if (val.Groups.Count < 5)
				return;
			approxVersion = new Version(int.Parse(val.Groups[1].ToString()),
										int.Parse(val.Groups[2].ToString()),
										int.Parse(val.Groups[3].ToString()),
										int.Parse(val.Groups[4].ToString()));
		}

		public override void DeobfuscateMethodEnd(Blocks blocks) {
			if (proxyCallFixer != null)
				proxyCallFixer.Deobfuscate(blocks);
			resourceDecrypter.Deobfuscate(blocks);
			unpacker.Deobfuscate(blocks);
			if (int32ValueInliner != null) {
				int32ValueInliner.Decrypt(blocks);
				int64ValueInliner.Decrypt(blocks);
				singleValueInliner.Decrypt(blocks);
				doubleValueInliner.Decrypt(blocks);
			}
			base.DeobfuscateMethodEnd(blocks);
		}

		public override void DeobfuscateEnd() {
			if (proxyCallFixer != null) {
				if (RemoveProxyDelegates(proxyCallFixer))
					AddFieldsToBeRemoved(proxyCallFixer.Fields, "Proxy delegate instance field");
				proxyCallFixer.CleanUp();
			}
			if (constantsDecrypterV18 != null)
				constantsDecrypterV18.CleanUp();

			if (CanRemoveStringDecrypterType) {
				if (stringDecrypter != null) {
					AddMethodToBeRemoved(stringDecrypter.Method, "String decrypter method");
					AddResourceToBeRemoved(stringDecrypter.Resource, "Encrypted strings");
				}
			}

			module.IsILOnly = true;

			base.DeobfuscateEnd();
		}

		public override IEnumerable<int> GetStringDecrypterMethods() {
			var list = new List<int>();
			if (stringDecrypter != null && stringDecrypter.Method != null)
				list.Add(stringDecrypter.Method.MDToken.ToInt32());
			if (constantsDecrypterV15 != null) {
				foreach (var info in constantsDecrypterV15.DecrypterInfos)
					list.Add(info.decryptMethod.MDToken.ToInt32());
			}
			if (constantsDecrypterV17 != null) {
				foreach (var info in constantsDecrypterV17.DecrypterInfos)
					list.Add(info.decryptMethod.MDToken.ToInt32());
			}
			if (constantsDecrypterV18 != null) {
				foreach (var info in constantsDecrypterV18.Decrypters)
					list.Add(info.method.MDToken.ToInt32());
			}
			return list;
		}

		string IStringDecrypter.ReadUserString(uint token) {
			if (jitMethodsDecrypter == null)
				return null;
			return ((IStringDecrypter)jitMethodsDecrypter).ReadUserString(token);
		}

		protected override void Dispose(bool disposing) {
			if (disposing) {
				if (proxyCallFixer != null)
					proxyCallFixer.Dispose();
				proxyCallFixer = null;
			}
			base.Dispose(disposing);
		}
	}
}
