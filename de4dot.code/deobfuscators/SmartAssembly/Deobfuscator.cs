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
using System.IO;
using System.Text;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.SmartAssembly {
	public class DeobfuscatorInfo : DeobfuscatorInfoBase {
		public const string THE_NAME = "SmartAssembly";
		public const string THE_TYPE = "sa";
		const string DEFAULT_REGEX = DeobfuscatorBase.DEFAULT_ASIAN_VALID_NAME_REGEX;
		BoolOption removeAutomatedErrorReporting;
		BoolOption removeTamperProtection;
		BoolOption removeMemoryManager;

		public DeobfuscatorInfo()
			: base(DEFAULT_REGEX) {
			removeAutomatedErrorReporting = new BoolOption(null, MakeArgName("error"), "Remove automated error reporting code", true);
			removeTamperProtection = new BoolOption(null, MakeArgName("tamper"), "Remove tamper protection code", true);
			removeMemoryManager = new BoolOption(null, MakeArgName("memory"), "Remove memory manager code", true);
		}

		public override string Name {
			get { return THE_NAME; }
		}

		public override string Type {
			get { return THE_TYPE; }
		}

		public override IDeobfuscator CreateDeobfuscator() {
			return new Deobfuscator(new Deobfuscator.Options {
				ValidNameRegex = validNameRegex.Get(),
				RemoveAutomatedErrorReporting = removeAutomatedErrorReporting.Get(),
				RemoveTamperProtection = removeTamperProtection.Get(),
				RemoveMemoryManager = removeMemoryManager.Get(),
			});
		}

		protected override IEnumerable<Option> GetOptionsInternal() {
			return new List<Option>() {
				removeAutomatedErrorReporting,
				removeTamperProtection,
				removeMemoryManager,
			};
		}
	}

	class Deobfuscator : DeobfuscatorBase {
		Options options;
		bool foundVersion = false;
		Version approxVersion = new Version(0, 0, 0, 0);
		bool canRemoveTypes;
		string poweredByAttributeString = null;
		string obfuscatorName = DeobfuscatorInfo.THE_NAME;
		bool foundSmartAssemblyAttribute = false;

		IList<StringDecrypterInfo> stringDecrypterInfos = new List<StringDecrypterInfo>();
		IList<StringDecrypter> stringDecrypters = new List<StringDecrypter>();
		ResourceDecrypterInfo resourceDecrypterInfo;
		ResourceDecrypter resourceDecrypter;
		AssemblyResolverInfo assemblyResolverInfo;
		AssemblyResolver assemblyResolver;
		ResourceResolverInfo resourceResolverInfo;
		ResourceResolver resourceResolver;
		MemoryManagerInfo memoryManagerInfo;

		ProxyCallFixer proxyCallFixer;
		AutomatedErrorReportingFinder automatedErrorReportingFinder;
		TamperProtectionRemover tamperProtectionRemover;

		internal class Options : OptionsBase {
			public bool RemoveAutomatedErrorReporting { get; set; }
			public bool RemoveTamperProtection { get; set; }
			public bool RemoveMemoryManager { get; set; }
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

		string ObfuscatorName {
			set {
				obfuscatorName = value;
				foundVersion = true;
			}
		}

		public Deobfuscator(Options options)
			: base(options) {
			this.options = options;
			StringFeatures = StringFeatures.AllowStaticDecryption;
		}

		public override void Initialize(ModuleDefMD module) {
			base.Initialize(module);
		}

		protected override int DetectInternal() {
			int val = 0;

			if (memoryManagerInfo.Detected)
				val += 100;
			if (foundSmartAssemblyAttribute)
				val += 10;

			return val;
		}

		protected override void ScanForObfuscator() {
			FindSmartAssemblyAttributes();
			memoryManagerInfo = new MemoryManagerInfo(module);
			memoryManagerInfo.Find();
			proxyCallFixer = new ProxyCallFixer(module, DeobfuscatedFile);
			proxyCallFixer.FindDelegateCreator(module);

			if (!foundVersion)
				GuessVersion();
		}

		void FindSmartAssemblyAttributes() {
			foreach (var type in module.Types) {
				if (Utils.StartsWith(type.FullName, "SmartAssembly.Attributes.PoweredByAttribute", StringComparison.Ordinal)) {
					foundSmartAssemblyAttribute = true;
					AddAttributeToBeRemoved(type, "Obfuscator attribute");
					InitializeVersion(type);
				}
			}
		}

		void InitializeVersion(TypeDef attr) {
			var s = DotNetUtils.GetCustomArgAsString(GetAssemblyAttribute(attr), 0);
			if (s == null)
				return;

			poweredByAttributeString = s;

			var val = System.Text.RegularExpressions.Regex.Match(s, @"^Powered by (SmartAssembly (\d+)\.(\d+)\.(\d+)\.(\d+))$");
			if (val.Groups.Count < 6)
				return;
			ObfuscatorName = val.Groups[1].ToString();
			approxVersion = new Version(int.Parse(val.Groups[2].ToString()),
										int.Parse(val.Groups[3].ToString()),
										int.Parse(val.Groups[4].ToString()),
										int.Parse(val.Groups[5].ToString()));
			return;
		}

		void GuessVersion() {
			if (poweredByAttributeString == "Powered by SmartAssembly") {
				ObfuscatorName = "SmartAssembly 5.0/5.1";
				approxVersion = new Version(5, 0, 0, 0);
				return;
			}

			if (poweredByAttributeString == "Powered by {smartassembly}") {
				// It's SA 1.x - 4.x

				if (proxyCallFixer.Detected || HasEmptyClassesInEveryNamespace()) {
					ObfuscatorName = "SmartAssembly 4.x";
					approxVersion = new Version(4, 0, 0, 0);
					return;
				}

				int ver = CheckTypeIdAttribute();
				if (ver == 2) {
					ObfuscatorName = "SmartAssembly 2.x";
					approxVersion = new Version(2, 0, 0, 0);
					return;
				}
				if (ver == 1) {
					ObfuscatorName = "SmartAssembly 1.x-2.x";
					approxVersion = new Version(1, 0, 0, 0);
					return;
				}

				if (HasModuleCctor()) {
					ObfuscatorName = "SmartAssembly 3.x";
					approxVersion = new Version(3, 0, 0, 0);
					return;
				}

				ObfuscatorName = "SmartAssembly 1.x-4.x";
				approxVersion = new Version(1, 0, 0, 0);
				return;
			}
		}

		int CheckTypeIdAttribute() {
			var type = GetTypeIdAttribute();
			if (type == null)
				return -1;

			var fields = type.Fields;
			if (fields.Count == 1)
				return 1;	// 1.x: int ID
			if (fields.Count == 2)
				return 2;	// 2.x: int ID, static int AssemblyID
			return -1;
		}

		TypeDef GetTypeIdAttribute() {
			Dictionary<TypeDef, bool> attrs = null;
			int counter = 0;
			foreach (var type in module.GetTypes()) {
				counter++;
				var cattrs = type.CustomAttributes;
				if (cattrs.Count == 0)
					return null;

				var attrs2 = new Dictionary<TypeDef, bool>();
				foreach (var cattr in cattrs) {
					if (!DotNetUtils.IsMethod(cattr.Constructor, "System.Void", "(System.Int32)"))
						continue;
					var attrType = cattr.AttributeType as TypeDef;
					if (attrType == null)
						continue;
					if (attrs != null && !attrs.ContainsKey(attrType))
						continue;
					attrs2[attrType] = true;
				}
				attrs = attrs2;

				if (attrs.Count == 0)
					return null;
				if (attrs.Count == 1 && counter >= 30)
					break;
			}

			if (attrs == null)
				return null;
			foreach (var type in attrs.Keys)
				return type;
			return null;
		}

		bool HasModuleCctor() {
			return DotNetUtils.GetModuleTypeCctor(module) != null;
		}

		bool HasEmptyClassesInEveryNamespace() {
			var namespaces = new Dictionary<string, int>(StringComparer.Ordinal);
			var moduleType = DotNetUtils.GetModuleType(module);
			foreach (var type in module.Types) {
				if (type == moduleType)
					continue;
				var ns = type.Namespace.String;
				if (!namespaces.ContainsKey(ns))
					namespaces[ns] = 0;
				if (type.Name != "" || type.IsPublic || type.HasFields || type.HasMethods || type.HasProperties || type.HasEvents)
					continue;
				namespaces[ns]++;
			}

			foreach (int count in namespaces.Values) {
				if (count < 1)
					return false;
			}
			return true;
		}

		public override void DeobfuscateBegin() {
			base.DeobfuscateBegin();

			tamperProtectionRemover = new TamperProtectionRemover(module);
			automatedErrorReportingFinder = new AutomatedErrorReportingFinder(module);
			automatedErrorReportingFinder.find();

			if (options.RemoveMemoryManager) {
				AddModuleCctorInitCallToBeRemoved(memoryManagerInfo.CctorInitMethod);
				AddCallToBeRemoved(module.EntryPoint, memoryManagerInfo.CctorInitMethod);
			}

			InitDecrypters();
			proxyCallFixer.Find();
		}

		void InitDecrypters() {
			assemblyResolverInfo = new AssemblyResolverInfo(module, DeobfuscatedFile, this);
			assemblyResolverInfo.FindTypes();
			resourceDecrypterInfo = new ResourceDecrypterInfo(module, assemblyResolverInfo.SimpleZipTypeMethod, DeobfuscatedFile);
			resourceResolverInfo = new ResourceResolverInfo(module, DeobfuscatedFile, this, assemblyResolverInfo);
			resourceResolverInfo.FindTypes();
			resourceDecrypter = new ResourceDecrypter(resourceDecrypterInfo);
			assemblyResolver = new AssemblyResolver(resourceDecrypter, assemblyResolverInfo);
			resourceResolver = new ResourceResolver(module, assemblyResolver, resourceResolverInfo);

			InitStringDecrypterInfos();
			assemblyResolverInfo.FindTypes();
			resourceResolverInfo.FindTypes();

			AddModuleCctorInitCallToBeRemoved(assemblyResolverInfo.CallResolverMethod);
			AddCallToBeRemoved(module.EntryPoint, assemblyResolverInfo.CallResolverMethod);
			AddModuleCctorInitCallToBeRemoved(resourceResolverInfo.CallResolverMethod);
			AddCallToBeRemoved(module.EntryPoint, resourceResolverInfo.CallResolverMethod);

			resourceDecrypterInfo.SetSimpleZipType(GetGlobalSimpleZipTypeMethod(), DeobfuscatedFile);

			if (!DecryptResources())
				throw new ApplicationException("Could not decrypt resources");

			DumpEmbeddedAssemblies();
		}

		void DumpEmbeddedAssemblies() {
			assemblyResolver.ResolveResources();
			foreach (var tuple in assemblyResolver.GetDecryptedResources()) {
				DeobfuscatedFile.CreateAssemblyFile(tuple.Item2, tuple.Item1.simpleName, null);
				AddResourceToBeRemoved(tuple.Item1.resource, string.Format("Embedded assembly: {0}", tuple.Item1.assemblyName));
			}
		}

		bool DecryptResources() {
			if (!resourceResolver.CanDecryptResource())
				return false;
			var info = resourceResolver.MergeResources();
			if (info == null)
				return true;
			AddResourceToBeRemoved(info.resource, "Encrypted resources");
			assemblyResolver.ResolveResources();
			return true;
		}

		MethodDef GetGlobalSimpleZipTypeMethod() {
			if (assemblyResolverInfo.SimpleZipTypeMethod != null)
				return assemblyResolverInfo.SimpleZipTypeMethod;
			foreach (var info in stringDecrypterInfos) {
				if (info.SimpleZipTypeMethod != null)
					return info.SimpleZipTypeMethod;
			}
			return null;
		}

		void InitStringDecrypterInfos() {
			var stringEncoderClassFinder = new StringEncoderClassFinder(module, DeobfuscatedFile);
			stringEncoderClassFinder.Find();
			foreach (var info in stringEncoderClassFinder.StringsEncoderInfos) {
				var sinfo = new StringDecrypterInfo(module, info.StringDecrypterClass) {
					GetStringDelegate = info.GetStringDelegate,
					StringsType = info.StringsType,
					CreateStringDelegateMethod = info.CreateStringDelegateMethod,
				};
				stringDecrypterInfos.Add(sinfo);
			}

			// There may be more than one string decrypter. The strings in the first one's
			// methods may be decrypted by the other string decrypter.

			var initd = new Dictionary<StringDecrypterInfo, bool>(stringDecrypterInfos.Count);
			while (initd.Count != stringDecrypterInfos.Count) {
				StringDecrypterInfo initdInfo = null;
				for (int i = 0; i < 2; i++) {
					foreach (var info in stringDecrypterInfos) {
						if (initd.ContainsKey(info))
							continue;
						if (info.Initialize(this, DeobfuscatedFile)) {
							resourceDecrypterInfo.SetSimpleZipType(info.SimpleZipTypeMethod, DeobfuscatedFile);
							initdInfo = info;
							break;
						}
					}
					if (initdInfo != null)
						break;

					assemblyResolverInfo.FindTypes();
					resourceResolverInfo.FindTypes();
					DecryptResources();
				}

				if (initdInfo == null)
					break;

				initd[initdInfo] = true;
				InitStringDecrypter(initdInfo);
			}

			// Sometimes there could be a string decrypter present that isn't called by anyone.
			foreach (var info in stringDecrypterInfos) {
				if (initd.ContainsKey(info))
					continue;
				Logger.v("String decrypter not initialized. Token {0:X8}", info.StringsEncodingClass.MDToken.ToInt32());
			}
		}

		void InitStringDecrypter(StringDecrypterInfo info) {
			Logger.v("Adding string decrypter. Resource: {0}", Utils.ToCsharpString(info.StringsResource.Name));
			var decrypter = new StringDecrypter(info);
			if (decrypter.CanDecrypt) {
				var invokeMethod = info.GetStringDelegate == null ? null : info.GetStringDelegate.FindMethod("Invoke");
				staticStringInliner.Add(invokeMethod, (method, gim, args) => {
					var fieldDef = DotNetUtils.GetField(module, (IField)args[0]);
					return decrypter.Decrypt(fieldDef.MDToken.ToInt32(), (int)args[1]);
				});
				staticStringInliner.Add(info.StringDecrypterMethod, (method, gim, args) => {
					return decrypter.Decrypt(0, (int)args[0]);
				});
			}
			stringDecrypters.Add(decrypter);
			DeobfuscatedFile.StringDecryptersAdded();
		}

		public override void DeobfuscateMethodEnd(Blocks blocks) {
			proxyCallFixer.Deobfuscate(blocks);
			RemoveAutomatedErrorReportingCode(blocks);
			RemoveTamperProtection(blocks);
			RemoveStringsInitCode(blocks);
			base.DeobfuscateMethodEnd(blocks);
		}

		public override void DeobfuscateEnd() {
			canRemoveTypes = FindBigType() == null;
			RemoveProxyDelegates(proxyCallFixer, canRemoveTypes);
			RemoveMemoryManagerStuff();
			RemoveTamperProtectionStuff();
			RemoveStringDecryptionStuff();
			RemoveResolverInfoTypes(assemblyResolverInfo, "Assembly");
			RemoveResolverInfoTypes(resourceResolverInfo, "Resource");
			base.DeobfuscateEnd();
		}

		TypeDef FindBigType() {
			if (approxVersion <= new Version(6, 5, 3, 53))
				return null;

			TypeDef bigType = null;
			foreach (var type in module.Types) {
				if (IsBigType(type)) {
					if (bigType == null || type.Methods.Count > bigType.Methods.Count)
						bigType = type;
				}
			}
			return bigType;
		}

		bool IsBigType(TypeDef type) {
			if (type.Methods.Count < 50)
				return false;
			if (type.HasProperties || type.HasEvents)
				return false;
			if (type.Fields.Count > 3)
				return false;
			foreach (var method in type.Methods) {
				if (!method.IsStatic)
					return false;
			}
			return true;
		}

		void RemoveResolverInfoTypes(ResolverInfoBase info, string typeName) {
			if (!canRemoveTypes)
				return;
			if (info.CallResolverType == null || info.Type == null)
				return;
			AddTypeToBeRemoved(info.CallResolverType, string.Format("{0} resolver type #1", typeName));
			AddTypeToBeRemoved(info.Type, string.Format("{0} resolver type #2", typeName));
		}

		void RemoveAutomatedErrorReportingCode(Blocks blocks) {
			if (!options.RemoveAutomatedErrorReporting)
				return;
			if (automatedErrorReportingFinder.Remove(blocks))
				Logger.v("Removed Automated Error Reporting code");
		}

		void RemoveTamperProtection(Blocks blocks) {
			if (!options.RemoveTamperProtection)
				return;
			if (tamperProtectionRemover.Remove(blocks))
				Logger.v("Removed Tamper Protection code");
		}

		void RemoveMemoryManagerStuff() {
			if (!canRemoveTypes || !options.RemoveMemoryManager)
				return;
			AddTypeToBeRemoved(memoryManagerInfo.Type, "Memory manager type");
		}

		void RemoveTamperProtectionStuff() {
			if (!options.RemoveTamperProtection)
				return;
			AddMethodsToBeRemoved(tamperProtectionRemover.PinvokeMethods, "Tamper protection PInvoke method");
		}

		void RemoveStringDecryptionStuff() {
			if (!CanRemoveStringDecrypterType)
				return;

			foreach (var decrypter in stringDecrypters) {
				var info = decrypter.StringDecrypterInfo;
				AddResourceToBeRemoved(info.StringsResource, "Encrypted strings");
				AddFieldsToBeRemoved(info.GetAllStringDelegateFields(), "String decrypter delegate field");

				if (canRemoveTypes) {
					AddTypeToBeRemoved(info.StringsEncodingClass, "String decrypter type");
					AddTypeToBeRemoved(info.StringsType, "Creates the string decrypter delegates");
					AddTypeToBeRemoved(info.GetStringDelegate, "String decrypter delegate type");
				}
			}
		}

		void RemoveStringsInitCode(Blocks blocks) {
			if (!CanRemoveStringDecrypterType)
				return;

			if (blocks.Method.Name == ".cctor") {
				foreach (var decrypter in stringDecrypters)
					decrypter.StringDecrypterInfo.RemoveInitCode(blocks);
			}
		}

		public override IEnumerable<int> GetStringDecrypterMethods() {
			var list = new List<int>();
			foreach (var method in staticStringInliner.Methods)
				list.Add(method.MDToken.ToInt32());
			return list;
		}
	}
}
