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

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;
using de4dot.blocks;

// SmartAssembly can add so much junk that it's very difficult to find and remove all of it.
// I remove some safe types that are almost guaranteed not to have any references in the code.

namespace de4dot.deobfuscators.SmartAssembly {
	class DeobfuscatorInfo : DeobfuscatorInfoBase {
		public const string THE_NAME = "SmartAssembly";
		public const string THE_TYPE = "sa";
		BoolOption removeAutomatedErrorReporting;
		BoolOption removeTamperProtection;
		BoolOption removeMemoryManager;

		public DeobfuscatorInfo()
			: base() {
			removeAutomatedErrorReporting = new BoolOption(null, makeArgName("error"), "Remove automated error reporting code", true);
			removeTamperProtection = new BoolOption(null, makeArgName("tamper"), "Remove tamper protection code", true);
			removeMemoryManager = new BoolOption(null, makeArgName("memory"), "Remove memory manager code", true);
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
				RemoveAutomatedErrorReporting = removeAutomatedErrorReporting.get(),
				RemoveTamperProtection = removeTamperProtection.get(),
				RemoveMemoryManager = removeMemoryManager.get(),
			});
		}

		protected override IEnumerable<Option> getOptionsInternal() {
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

		ProxyDelegateFinder proxyDelegateFinder;
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

		public override void init(ModuleDefinition module) {
			base.init(module);
			automatedErrorReportingFinder = new AutomatedErrorReportingFinder(module);
			tamperProtectionRemover = new TamperProtectionRemover(module);
		}

		protected override int detectInternal() {
			int val = 0;

			if (foundSmartAssemblyAttribute)
				val += 100;

			// Since we don't remove this type, we will detect it even when we've deobfuscated
			// an assembly. Don't use this code for now. When the type is removed, this code
			// should be re-enabled.
// 			if (automatedErrorReportingFinder.Detected)
// 				val += 10;

			if (memoryManagerInfo.Detected)
				val += 10;

			return val;
		}

		protected override void scanForObfuscator() {
			proxyDelegateFinder = new ProxyDelegateFinder(module, DeobfuscatedFile);
			findSmartAssemblyAttributes();
			findAutomatedErrorReportingType();
			memoryManagerInfo = new MemoryManagerInfo(module);
			proxyDelegateFinder.findDelegateCreator(module);

			if (!foundVersion)
				guessVersion();
		}

		void findSmartAssemblyAttributes() {
			foreach (var type in module.Types) {
				if (Utils.StartsWith(type.FullName, "SmartAssembly.Attributes.PoweredByAttribute", StringComparison.Ordinal)) {
					foundSmartAssemblyAttribute = true;
					addAttributeToBeRemoved(type, "Obfuscator attribute");
					initializeVersion(type);
				}
			}
		}

		void initializeVersion(TypeDefinition attr) {
			var s = DotNetUtils.getCustomArgAsString(getAssemblyAttribute(attr), 0);
			if (s == null)
				return;

			poweredByAttributeString = s;

			var val = System.Text.RegularExpressions.Regex.Match(s, @"^Powered by (SmartAssembly \d+\.\d+\.\d+\.\d+)$");
			if (val.Groups.Count < 2)
				return;
			ObfuscatorName = val.Groups[1].ToString();
			return;
		}

		void guessVersion() {
			if (poweredByAttributeString == "Powered by SmartAssembly") {
				ObfuscatorName = "SmartAssembly 5.0/5.1";
				return;
			}

			if (poweredByAttributeString == "Powered by {smartassembly}") {
				// It's SA 1.x - 4.x

				if (hasEmptyClassesInEveryNamespace() || proxyDelegateFinder.Detected) {
					ObfuscatorName = "SmartAssembly 4.x";
					return;
				}

				int ver = checkTypeIdAttribute();
				if (ver == 2) {
					ObfuscatorName = "SmartAssembly 2.x";
					return;
				}
				if (ver == 1) {
					ObfuscatorName = "SmartAssembly 1.x-2.x";
					return;
				}

				if (hasModuleCctor()) {
					ObfuscatorName = "SmartAssembly 3.x";
					return;
				}

				ObfuscatorName = "SmartAssembly 1.x-4.x";
				return;
			}
		}

		int checkTypeIdAttribute() {
			var type = getTypeIdAttribute();
			if (type == null)
				return -1;

			var fields = type.Fields;
			if (fields.Count == 1)
				return 1;	// 1.x: int ID
			if (fields.Count == 2)
				return 2;	// 2.x: int ID, static int AssemblyID
			return -1;
		}

		TypeDefinition getTypeIdAttribute() {
			Dictionary<TypeDefinition, bool> attrs = null;
			int counter = 0;
			foreach (var type in module.GetTypes()) {
				counter++;
				var cattrs = type.CustomAttributes;
				if (cattrs.Count == 0)
					return null;

				var attrs2 = new Dictionary<TypeDefinition, bool>();
				foreach (var cattr in cattrs) {
					if (!DotNetUtils.isMethod(cattr.Constructor, "System.Void", "(System.Int32)"))
						continue;
					var attrType = cattr.AttributeType as TypeDefinition;
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

		bool hasModuleCctor() {
			var type = DotNetUtils.getModuleType(module);
			if (type == null)
				return false;
			return DotNetUtils.getMethod(type, ".cctor") != null;
		}

		bool hasEmptyClassesInEveryNamespace() {
			var namespaces = new Dictionary<string, int>(StringComparer.Ordinal);
			foreach (var type in module.Types) {
				if (type.FullName == "<Module>")
					continue;
				var ns = type.Namespace;
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

		void findAutomatedErrorReportingType() {
			automatedErrorReportingFinder.find();
		}

		public override void deobfuscateBegin() {
			base.deobfuscateBegin();
			if (options.RemoveMemoryManager) {
				addModuleCctorInitCallToBeRemoved(memoryManagerInfo.CctorInitMethod);
				addCallToBeRemoved(module.EntryPoint, memoryManagerInfo.CctorInitMethod);
			}
			initDecrypters();
			proxyDelegateFinder.find();
		}

		void initDecrypters() {
			assemblyResolverInfo = new AssemblyResolverInfo(module, DeobfuscatedFile, this);
			assemblyResolverInfo.findTypes();
			resourceDecrypterInfo = new ResourceDecrypterInfo(module, assemblyResolverInfo.SimpleZipType, DeobfuscatedFile);
			resourceResolverInfo = new ResourceResolverInfo(module, DeobfuscatedFile, this, assemblyResolverInfo);
			resourceResolverInfo.findTypes();
			resourceDecrypter = new ResourceDecrypter(resourceDecrypterInfo);
			assemblyResolver = new AssemblyResolver(resourceDecrypter, assemblyResolverInfo);
			resourceResolver = new ResourceResolver(module, assemblyResolver, resourceResolverInfo);

			initStringDecrypterInfos();
			assemblyResolverInfo.findTypes();
			resourceResolverInfo.findTypes();

			addModuleCctorInitCallToBeRemoved(assemblyResolverInfo.CallResolverMethod);
			addModuleCctorInitCallToBeRemoved(resourceResolverInfo.CallResolverMethod);

			resourceDecrypterInfo.setSimpleZipType(getGlobalSimpleZipType(), DeobfuscatedFile);

			if (!decryptResources())
				throw new ApplicationException("Could not decrypt resources");

			dumpEmbeddedAssemblies();
		}

		void dumpEmbeddedAssemblies() {
			assemblyResolver.resolveResources();
			foreach (var tuple in assemblyResolver.getDecryptedResources()) {
				DeobfuscatedFile.createAssemblyFile(tuple.Item2, tuple.Item1.simpleName);
				addResourceToBeRemoved(tuple.Item1.resource, string.Format("Embedded assembly: {0}", tuple.Item1.assemblyName));
			}
		}

		bool decryptResources() {
			if (!resourceResolver.canDecryptResource())
				return false;
			var info = resourceResolver.mergeResources();
			if (info == null)
				return true;
			addResourceToBeRemoved(info.resource, "Encrypted resources");
			assemblyResolver.resolveResources();
			return true;
		}

		TypeDefinition getGlobalSimpleZipType() {
			if (assemblyResolverInfo.SimpleZipType != null)
				return assemblyResolverInfo.SimpleZipType;
			foreach (var info in stringDecrypterInfos) {
				if (info.SimpleZipType != null)
					return info.SimpleZipType;
			}
			return null;
		}

		void initStringDecrypterInfos() {
			var stringEncoderClassFinder = new StringEncoderClassFinder(module, DeobfuscatedFile);
			stringEncoderClassFinder.find();
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
						if (info.init(this, DeobfuscatedFile)) {
							resourceDecrypterInfo.setSimpleZipType(info.SimpleZipType, DeobfuscatedFile);
							initdInfo = info;
							break;
						}
					}
					if (initdInfo != null)
						break;

					assemblyResolverInfo.findTypes();
					resourceResolverInfo.findTypes();
					decryptResources();
				}

				if (initdInfo == null)
					throw new ApplicationException("Could not initialize all stringDecrypterInfos");

				initd[initdInfo] = true;
				initStringDecrypter(initdInfo);
			}
		}

		void initStringDecrypter(StringDecrypterInfo info) {
			Log.v("Adding string decrypter. Resource: {0}", Utils.toCsharpString(info.StringsResource.Name));
			var decrypter = new StringDecrypter(info);
			if (decrypter.CanDecrypt) {
				staticStringDecrypter.add(DotNetUtils.getMethod(info.GetStringDelegate, "Invoke"), (method, args) => {
					var fieldDefinition = DotNetUtils.getField(module, (FieldReference)args[0]);
					return decrypter.decrypt(fieldDefinition.MetadataToken.ToInt32(), (int)args[1]);
				});
				staticStringDecrypter.add(info.StringDecrypterMethod, (method, args) => {
					return decrypter.decrypt(0, (int)args[0]);
				});
			}
			stringDecrypters.Add(decrypter);
			DeobfuscatedFile.stringDecryptersAdded();
		}

		public override void deobfuscateMethodEnd(Blocks blocks) {
			proxyDelegateFinder.deobfuscate(blocks);
			removeAutomatedErrorReportingCode(blocks);
			removeTamperProtection(blocks);
			removeStringsInitCode(blocks);
			base.deobfuscateMethodEnd(blocks);
		}

		public override void deobfuscateEnd() {
			removeProxyDelegates(proxyDelegateFinder);
			removeMemoryManagerStuff();
			removeTamperProtectionStuff();
			removeStringDecryptionStuff();
			removeResolverInfoTypes(assemblyResolverInfo, "Assembly");
			removeResolverInfoTypes(resourceResolverInfo, "Resource");
			base.deobfuscateEnd();
		}

		void removeResolverInfoTypes(ResolverInfoBase info, string typeName) {
			addTypeToBeRemoved(info.CallResolverType, string.Format("{0} resolver type #1", typeName));
			addTypeToBeRemoved(info.Type, string.Format("{0} resolver type #2", typeName));
		}

		void removeAutomatedErrorReportingCode(Blocks blocks) {
			if (!options.RemoveAutomatedErrorReporting)
				return;
			if (automatedErrorReportingFinder.ExceptionLoggerRemover.remove(blocks))
				Log.v("Removed Automated Error Reporting code");
		}

		void removeTamperProtection(Blocks blocks) {
			if (!options.RemoveTamperProtection)
				return;
			if (tamperProtectionRemover.remove(blocks))
				Log.v("Removed Tamper Protection code");
		}

		void removeMemoryManagerStuff() {
			if (!options.RemoveMemoryManager)
				return;
			addTypeToBeRemoved(memoryManagerInfo.Type, "Memory manager type");
		}

		void removeTamperProtectionStuff() {
			if (!options.RemoveTamperProtection)
				return;
			addMethodsToBeRemoved(tamperProtectionRemover.PinvokeMethods, "Tamper protection PInvoke method");
		}

		bool canRemoveStringDecrypterStuff() {
			return Operations.DecryptStrings != OpDecryptString.None;
		}

		void removeStringDecryptionStuff() {
			if (!canRemoveStringDecrypterStuff())
				return;

			foreach (var decrypter in stringDecrypters) {
				var info = decrypter.StringDecrypterInfo;
				addResourceToBeRemoved(info.StringsResource, "Encrypted strings");
				addFieldsToBeRemoved(info.getAllStringDelegateFields(), "String decrypter delegate field");

				addTypeToBeRemoved(info.StringsEncodingClass, "String decrypter type");
				addTypeToBeRemoved(info.StringsType, "Creates the string decrypter delegates");
				addTypeToBeRemoved(info.GetStringDelegate, "String decrypter delegate type");
			}
		}

		void removeStringsInitCode(Blocks blocks) {
			if (!canRemoveStringDecrypterStuff())
				return;

			if (blocks.Method.Name == ".cctor") {
				foreach (var decrypter in stringDecrypters)
					decrypter.StringDecrypterInfo.removeInitCode(blocks);
			}
		}

		public override IEnumerable<string> getStringDecrypterMethods() {
			var list = new List<string>();
			foreach (var method in staticStringDecrypter.Methods)
				list.Add(method.MetadataToken.ToInt32().ToString("X8"));
			return list;
		}
	}
}
