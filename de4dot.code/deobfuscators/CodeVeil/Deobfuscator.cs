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

namespace de4dot.code.deobfuscators.CodeVeil {
	public class DeobfuscatorInfo : DeobfuscatorInfoBase {
		public const string THE_NAME = "CodeVeil";
		public const string THE_TYPE = "cv";
		const string DEFAULT_REGEX = @"!^[A-Za-z]{1,2}$&" + DeobfuscatorBase.DEFAULT_ASIAN_VALID_NAME_REGEX;

		public DeobfuscatorInfo()
			: base(DEFAULT_REGEX) {
		}

		public override string Name => THE_NAME;
		public override string Type => THE_TYPE;

		public override IDeobfuscator CreateDeobfuscator() =>
			new Deobfuscator(new Deobfuscator.Options {
				ValidNameRegex = validNameRegex.Get(),
			});

		protected override IEnumerable<Option> GetOptionsInternal() => new List<Option>();
	}

	class Deobfuscator : DeobfuscatorBase {
		Options options;
		string obfuscatorName = DeobfuscatorInfo.THE_NAME;

		MainType mainType;
		MethodsDecrypter methodsDecrypter;
		ProxyCallFixer proxyCallFixer;
		StringDecrypter stringDecrypter;
		AssemblyResolver assemblyResolver;
		TypeDef killType;
		ResourceDecrypter resourceDecrypter;

		internal class Options : OptionsBase {
		}

		public override string Type => DeobfuscatorInfo.THE_TYPE;
		public override string TypeLong => DeobfuscatorInfo.THE_NAME;
		public override string Name => obfuscatorName;

		public Deobfuscator(Options options)
			: base(options) {
			this.options = options;
			StringFeatures = StringFeatures.AllowStaticDecryption | StringFeatures.AllowDynamicDecryption;
		}

		protected override int DetectInternal() {
			int val = 0;

			int sum = ToInt32(mainType.Detected) +
					ToInt32(methodsDecrypter.Detected) +
					ToInt32(stringDecrypter.Detected) +
					ToInt32(proxyCallFixer.Detected);
			if (sum > 0)
				val += 100 + 10 * (sum - 1);

			return val;
		}

		protected override void ScanForObfuscator() {
			FindKillType();
			mainType = new MainType(module);
			mainType.Find();
			proxyCallFixer = new ProxyCallFixer(module, mainType);
			proxyCallFixer.FindDelegateCreator();
			methodsDecrypter = new MethodsDecrypter(mainType);
			methodsDecrypter.Find();
			stringDecrypter = new StringDecrypter(module, mainType);
			stringDecrypter.Find();
			var version = DetectVersion();
			if (!string.IsNullOrEmpty(version))
				obfuscatorName = obfuscatorName + " " + version;
		}

		string DetectVersion() {
			if (mainType.Detected) {
				switch (mainType.Version) {
				case ObfuscatorVersion.Unknown:
					return null;

				case ObfuscatorVersion.V3:
					return "3.x";

				case ObfuscatorVersion.V4_0:
					return "4.0";

				case ObfuscatorVersion.V4_1:
					return "4.1";

				case ObfuscatorVersion.V5_0:
					return "5.0";

				default:
					throw new ApplicationException("Unknown version");
				}
			}

			return null;
		}

		void FindKillType() {
			foreach (var type in module.Types) {
				if (type.FullName == "____KILL") {
					killType = type;
					break;
				}
			}
		}

		public override bool GetDecryptedModule(int count, ref byte[] newFileData, ref DumpedMethods dumpedMethods) {
			if (count != 0 || !methodsDecrypter.Detected)
				return false;

			var fileData = DeobUtils.ReadModule(module);
			if (!methodsDecrypter.Decrypt(fileData, ref dumpedMethods))
				return false;

			newFileData = fileData;
			return true;
		}

		public override IDeobfuscator ModuleReloaded(ModuleDefMD module) {
			var newOne = new Deobfuscator(options);
			newOne.SetModule(module);
			newOne.mainType = new MainType(module, mainType);
			newOne.methodsDecrypter = new MethodsDecrypter(mainType, methodsDecrypter);
			newOne.stringDecrypter = new StringDecrypter(module, newOne.mainType, stringDecrypter);
			newOne.proxyCallFixer = new ProxyCallFixer(module, newOne.mainType, proxyCallFixer);
			newOne.killType = DeobUtils.Lookup(module, killType, "Could not find KILL type");
			return newOne;
		}

		public override void DeobfuscateBegin() {
			base.DeobfuscateBegin();

			AddTypeToBeRemoved(killType, "KILL type");

			mainType.Initialize();
			foreach (var initMethod in mainType.OtherInitMethods) {
				AddCctorInitCallToBeRemoved(initMethod);
				AddCtorInitCallToBeRemoved(initMethod);
			}

			if (Operations.DecryptStrings != OpDecryptString.None) {
				stringDecrypter.Initialize();
				staticStringInliner.Add(stringDecrypter.DecryptMethod, (method, gim, args) => {
					return stringDecrypter.Decrypt((int)args[0]);
				});
				DeobfuscatedFile.StringDecryptersAdded();
				AddModuleCctorInitCallToBeRemoved(stringDecrypter.InitMethod);
				AddCallToBeRemoved(mainType.GetInitStringDecrypterMethod(stringDecrypter.InitMethod), stringDecrypter.InitMethod);
			}

			assemblyResolver = new AssemblyResolver(module);
			assemblyResolver.Initialize();
			DumpEmbeddedAssemblies();

			RemoveTamperDetection();

			proxyCallFixer.Initialize();
			proxyCallFixer.Find();

			resourceDecrypter = new ResourceDecrypter(module);
			resourceDecrypter.Initialize();
			resourceDecrypter.Decrypt();
			if (resourceDecrypter.CanRemoveTypes) {
				AddTypeToBeRemoved(resourceDecrypter.ResourceFlagsType, "Obfuscator ResourceFlags type");
				AddTypeToBeRemoved(resourceDecrypter.ResType, "Obfuscator Res type");
				AddTypeToBeRemoved(resourceDecrypter.ResourceEnumeratorType, "Obfuscator ResourceEnumerator type");
				AddTypeToBeRemoved(resourceDecrypter.EncryptedResourceReaderType, "Obfuscator EncryptedResourceReader type");
				AddTypeToBeRemoved(resourceDecrypter.EncryptedResourceSetType, "Obfuscator EncryptedResourceSet type");
				AddTypeToBeRemoved(resourceDecrypter.EncryptedResourceStreamType, "Obfuscator EncryptedResourceStream type");
			}
		}

		void RemoveTamperDetection() {
			var tamperDetection = new TamperDetection(module, mainType);
			tamperDetection.Initialize();
			foreach (var tamperDetectionMethod in tamperDetection.Methods)
				AddCctorInitCallToBeRemoved(tamperDetectionMethod);
			AddTypeToBeRemoved(tamperDetection.Type, "Tamper detection type");
		}

		void DumpEmbeddedAssemblies() {
			foreach (var info in assemblyResolver.AssemblyInfos)
				DeobfuscatedFile.CreateAssemblyFile(info.data, info.simpleName, info.extension);
			AddResourceToBeRemoved(assemblyResolver.BundleDataResource, "Embedded assemblies resource");
			AddResourceToBeRemoved(assemblyResolver.BundleXmlFileResource, "Embedded assemblies XML file resource");
			AddTypesToBeRemoved(assemblyResolver.BundleTypes, "Obfuscator assembly bundle types");
		}

		public override void DeobfuscateMethodBegin(Blocks blocks) {
			proxyCallFixer.Deobfuscate(blocks);
			base.DeobfuscateMethodBegin(blocks);
		}

		public override void DeobfuscateMethodEnd(Blocks blocks) {
			mainType.RemoveInitCall(blocks);
			resourceDecrypter.Deobfuscate(blocks);
			base.DeobfuscateMethodEnd(blocks);
		}

		public override void DeobfuscateEnd() {
			bool canRemoveProxyTypes = proxyCallFixer.CanRemoveTypes;

			if (CanRemoveStringDecrypterType)
				AddTypeToBeRemoved(stringDecrypter.Type, "String decrypter type");

			if (!mainType.Detected) {
			}
			else if (mainType.Version >= ObfuscatorVersion.V5_0) {
				if (!proxyCallFixer.FoundProxyType || canRemoveProxyTypes)
					AddTypeToBeRemoved(mainType.Type, "Main CV type");
			}
			else {
				var type = mainType.Type;
				if (!type.HasNestedTypes && !type.HasProperties && !type.HasEvents && !type.HasFields)
					AddTypeToBeRemoved(type, "Main CV type");
				else {
					foreach (var method in type.Methods)
						AddMethodToBeRemoved(method, "CV main type method");
				}
			}

			RemoveProxyDelegates(proxyCallFixer, canRemoveProxyTypes);
			if (canRemoveProxyTypes) {
				AddTypeToBeRemoved(proxyCallFixer.IlGeneratorType, "Obfuscator proxy method ILGenerator type");
				AddTypeToBeRemoved(proxyCallFixer.FieldInfoType, "Obfuscator proxy method FieldInfo type");
				AddTypeToBeRemoved(proxyCallFixer.MethodInfoType, "Obfuscator proxy method MethodInfo type");
			}

			AddMethodsToBeRemoved(InvalidMethodsFinder.FindAll(module), "Anti-reflection method");

			base.DeobfuscateEnd();
		}

		public override IEnumerable<int> GetStringDecrypterMethods() {
			var list = new List<int>();
			if (stringDecrypter.DecryptMethod != null)
				list.Add(stringDecrypter.DecryptMethod.MDToken.ToInt32());
			return list;
		}
	}
}
