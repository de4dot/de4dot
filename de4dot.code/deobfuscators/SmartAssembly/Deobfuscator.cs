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
		BoolOption removeAutomatedErrorReporting;
		BoolOption removeTamperProtection;
		BoolOption removeMemoryManager;

		public DeobfuscatorInfo()
			: base("sa") {
			removeAutomatedErrorReporting = new BoolOption(null, makeArgName("error"), "Remove automated error reporting code", true);
			removeTamperProtection = new BoolOption(null, makeArgName("tamper"), "Remove tamper protection code", true);
			removeMemoryManager = new BoolOption(null, makeArgName("memory"), "Remove memory manager code", true);
		}

		internal static string ObfuscatorType {
			get { return "SmartAssembly"; }
		}

		public override string Type {
			get { return ObfuscatorType; }
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
		string obfuscatorName = "SmartAssembly";
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
			get { return DeobfuscatorInfo.ObfuscatorType; }
		}

		public override string Name {
			get { return obfuscatorName; }
		}

		public Deobfuscator(Options options)
			: base(options) {
			this.options = options;
			StringFeatures = StringFeatures.AllowStaticDecryption;
		}

		public override void init(ModuleDefinition module, IList<MemberReference> memberReferences) {
			base.init(module, memberReferences);
			proxyDelegateFinder = new ProxyDelegateFinder(module, memberReferences);
			automatedErrorReportingFinder = new AutomatedErrorReportingFinder(module);
			tamperProtectionRemover = new TamperProtectionRemover(module);
		}

		public override int detect() {
			scanForObfuscator();

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

		protected override void scanForObfuscatorInternal() {
			findSmartAssemblyAttributes();
			findAutomatedErrorReportingType();
			memoryManagerInfo = new MemoryManagerInfo(module);
			proxyDelegateFinder.findDelegateCreator(module);
		}

		void findSmartAssemblyAttributes() {
			foreach (var type in module.Types) {
				if (type.FullName.StartsWith("SmartAssembly.Attributes.PoweredByAttribute", StringComparison.Ordinal)) {
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

			var val = System.Text.RegularExpressions.Regex.Match(s, @"^Powered by (SmartAssembly \d+\.\d+\.\d+\.\d+)$");
			if (val.Groups.Count < 2)
				return;
			obfuscatorName = val.Groups[1].ToString();
		}

		void findAutomatedErrorReportingType() {
			automatedErrorReportingFinder.find();
		}

		public override void deobfuscateBegin() {
			base.deobfuscateBegin();
			if (options.RemoveMemoryManager)
				addCctorInitCallToBeRemoved(memoryManagerInfo.CctorInitMethod);
			initDecrypters();
			proxyDelegateFinder.find();
		}

		void initDecrypters() {
			assemblyResolverInfo = new AssemblyResolverInfo(module, DeobfuscatedFile, this);
			resourceDecrypterInfo = new ResourceDecrypterInfo(module, assemblyResolverInfo.SimpleZipType, DeobfuscatedFile);
			resourceResolverInfo = new ResourceResolverInfo(module, DeobfuscatedFile, this);
			resourceDecrypter = new ResourceDecrypter(resourceDecrypterInfo);
			assemblyResolver = new AssemblyResolver(resourceDecrypter, assemblyResolverInfo);
			resourceResolver = new ResourceResolver(module, assemblyResolver, resourceResolverInfo);

			initStringDecrypterInfos();
			assemblyResolverInfo.findTypes();
			resourceResolverInfo.findTypes();

			addCctorInitCallToBeRemoved(assemblyResolverInfo.CallResolverMethod);
			addCctorInitCallToBeRemoved(resourceResolverInfo.CallResolverMethod);

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
			var rsrc = resourceResolver.mergeResources();
			if (rsrc == null)
				return true;
			addResourceToBeRemoved(rsrc, "Encrypted resources");
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
			addTypeToBeRemoved(info.ResolverType, string.Format("{0} resolver type #2", typeName));
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
			addTypeToBeRemoved(memoryManagerInfo.MemoryManagerType, "Memory manager type");
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
