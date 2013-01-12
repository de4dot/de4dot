/*
    Copyright (C) 2011-2013 de4dot@gmail.com

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
		const string DEFAULT_REGEX = @"!^[A-Za-z]{1,2}$&" + DeobfuscatorBase.DEFAULT_VALID_NAME_REGEX;

		public DeobfuscatorInfo()
			: base(DEFAULT_REGEX) {
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

		MainType mainType;
		MethodsDecrypter methodsDecrypter;
		ProxyCallFixer proxyCallFixer;
		StringDecrypter stringDecrypter;
		AssemblyResolver assemblyResolver;
		TypeDef killType;
		ResourceDecrypter resourceDecrypter;

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

		public Deobfuscator(Options options)
			: base(options) {
			this.options = options;
			StringFeatures = StringFeatures.AllowStaticDecryption | StringFeatures.AllowDynamicDecryption;
		}

		protected override int detectInternal() {
			int val = 0;

			int sum = toInt32(mainType.Detected) +
					toInt32(methodsDecrypter.Detected) +
					toInt32(stringDecrypter.Detected) +
					toInt32(proxyCallFixer.Detected);
			if (sum > 0)
				val += 100 + 10 * (sum - 1);

			return val;
		}

		protected override void scanForObfuscator() {
			findKillType();
			mainType = new MainType(module);
			mainType.find();
			proxyCallFixer = new ProxyCallFixer(module, mainType);
			proxyCallFixer.findDelegateCreator();
			methodsDecrypter = new MethodsDecrypter(mainType);
			methodsDecrypter.find();
			stringDecrypter = new StringDecrypter(module, mainType);
			stringDecrypter.find();
			var version = detectVersion();
			if (!string.IsNullOrEmpty(version))
				obfuscatorName = obfuscatorName + " " + version;
		}

		string detectVersion() {
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

		void findKillType() {
			foreach (var type in module.Types) {
				if (type.FullName == "____KILL") {
					killType = type;
					break;
				}
			}
		}

		public override bool getDecryptedModule(int count, ref byte[] newFileData, ref DumpedMethods dumpedMethods) {
			if (count != 0 || !methodsDecrypter.Detected)
				return false;

			var fileData = DeobUtils.readModule(module);
			if (!methodsDecrypter.decrypt(fileData, ref dumpedMethods))
				return false;

			newFileData = fileData;
			return true;
		}

		public override IDeobfuscator moduleReloaded(ModuleDefMD module) {
			var newOne = new Deobfuscator(options);
			newOne.setModule(module);
			newOne.mainType = new MainType(module, mainType);
			newOne.methodsDecrypter = new MethodsDecrypter(mainType, methodsDecrypter);
			newOne.stringDecrypter = new StringDecrypter(module, newOne.mainType, stringDecrypter);
			newOne.proxyCallFixer = new ProxyCallFixer(module, newOne.mainType, proxyCallFixer);
			newOne.killType = DeobUtils.lookup(module, killType, "Could not find KILL type");
			return newOne;
		}

		public override void deobfuscateBegin() {
			base.deobfuscateBegin();

			addTypeToBeRemoved(killType, "KILL type");

			mainType.initialize();
			foreach (var initMethod in mainType.OtherInitMethods) {
				addCctorInitCallToBeRemoved(initMethod);
				addCtorInitCallToBeRemoved(initMethod);
			}

			if (Operations.DecryptStrings != OpDecryptString.None) {
				stringDecrypter.initialize();
				staticStringInliner.add(stringDecrypter.DecryptMethod, (method, gim, args) => {
					return stringDecrypter.decrypt((int)args[0]);
				});
				DeobfuscatedFile.stringDecryptersAdded();
				addModuleCctorInitCallToBeRemoved(stringDecrypter.InitMethod);
				addCallToBeRemoved(mainType.getInitStringDecrypterMethod(stringDecrypter.InitMethod), stringDecrypter.InitMethod);
			}

			assemblyResolver = new AssemblyResolver(module);
			assemblyResolver.initialize();
			dumpEmbeddedAssemblies();

			removeTamperDetection();

			proxyCallFixer.initialize();
			proxyCallFixer.find();

			resourceDecrypter = new ResourceDecrypter(module);
			resourceDecrypter.initialize();
			resourceDecrypter.decrypt();
			if (resourceDecrypter.CanRemoveTypes) {
				addTypeToBeRemoved(resourceDecrypter.ResourceFlagsType, "Obfuscator ResourceFlags type");
				addTypeToBeRemoved(resourceDecrypter.ResType, "Obfuscator Res type");
				addTypeToBeRemoved(resourceDecrypter.ResourceEnumeratorType, "Obfuscator ResourceEnumerator type");
				addTypeToBeRemoved(resourceDecrypter.EncryptedResourceReaderType, "Obfuscator EncryptedResourceReader type");
				addTypeToBeRemoved(resourceDecrypter.EncryptedResourceSetType, "Obfuscator EncryptedResourceSet type");
				addTypeToBeRemoved(resourceDecrypter.EncryptedResourceStreamType, "Obfuscator EncryptedResourceStream type");
			}
		}

		void removeTamperDetection() {
			var tamperDetection = new TamperDetection(module, mainType);
			tamperDetection.initialize();
			foreach (var tamperDetectionMethod in tamperDetection.Methods)
				addCctorInitCallToBeRemoved(tamperDetectionMethod);
			addTypeToBeRemoved(tamperDetection.Type, "Tamper detection type");
		}

		void dumpEmbeddedAssemblies() {
			foreach (var info in assemblyResolver.AssemblyInfos)
				DeobfuscatedFile.createAssemblyFile(info.data, info.simpleName, info.extension);
			addResourceToBeRemoved(assemblyResolver.BundleDataResource, "Embedded assemblies resource");
			addResourceToBeRemoved(assemblyResolver.BundleXmlFileResource, "Embedded assemblies XML file resource");
			addTypesToBeRemoved(assemblyResolver.BundleTypes, "Obfuscator assembly bundle types");
		}

		public override void deobfuscateMethodBegin(Blocks blocks) {
			proxyCallFixer.deobfuscate(blocks);
			base.deobfuscateMethodBegin(blocks);
		}

		public override void deobfuscateMethodEnd(Blocks blocks) {
			mainType.removeInitCall(blocks);
			resourceDecrypter.deobfuscate(blocks);
			base.deobfuscateMethodEnd(blocks);
		}

		public override void deobfuscateEnd() {
			bool canRemoveProxyTypes = proxyCallFixer.CanRemoveTypes;

			if (CanRemoveStringDecrypterType)
				addTypeToBeRemoved(stringDecrypter.Type, "String decrypter type");

			if (!mainType.Detected) {
			}
			else if (mainType.Version >= ObfuscatorVersion.V5_0) {
				if (!proxyCallFixer.FoundProxyType || canRemoveProxyTypes)
					addTypeToBeRemoved(mainType.Type, "Main CV type");
			}
			else {
				var type = mainType.Type;
				if (!type.HasNestedTypes && !type.HasProperties && !type.HasEvents && !type.HasFields)
					addTypeToBeRemoved(type, "Main CV type");
				else {
					foreach (var method in type.Methods)
						addMethodToBeRemoved(method, "CV main type method");
				}
			}

			removeProxyDelegates(proxyCallFixer, canRemoveProxyTypes);
			if (canRemoveProxyTypes) {
				addTypeToBeRemoved(proxyCallFixer.IlGeneratorType, "Obfuscator proxy method ILGenerator type");
				addTypeToBeRemoved(proxyCallFixer.FieldInfoType, "Obfuscator proxy method FieldInfo type");
				addTypeToBeRemoved(proxyCallFixer.MethodInfoType, "Obfuscator proxy method MethodInfo type");
			}

			addMethodsToBeRemoved(InvalidMethodsFinder.findAll(module), "Anti-reflection method");

			base.deobfuscateEnd();
		}

		public override IEnumerable<int> getStringDecrypterMethods() {
			var list = new List<int>();
			if (stringDecrypter.DecryptMethod != null)
				list.Add(stringDecrypter.DecryptMethod.MDToken.ToInt32());
			return list;
		}
	}
}
