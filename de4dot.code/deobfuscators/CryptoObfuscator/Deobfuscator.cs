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
using System.Text.RegularExpressions;
using Mono.Cecil;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.CryptoObfuscator {
	public class DeobfuscatorInfo : DeobfuscatorInfoBase {
		public const string THE_NAME = "Crypto Obfuscator";
		public const string THE_TYPE = "co";
		const string DEFAULT_REGEX = @"!^(get_|set_|add_|remove_)?[A-Z]{1,3}(?:`\d+)?$&!^(get_|set_|add_|remove_)?c[0-9a-f]{32}(?:`\d+)?$&" + DeobfuscatorBase.DEFAULT_VALID_NAME_REGEX;
		BoolOption removeTamperProtection;
		BoolOption decryptConstants;

		public DeobfuscatorInfo()
			: base(DEFAULT_REGEX) {
			removeTamperProtection = new BoolOption(null, makeArgName("tamper"), "Remove tamper protection code", true);
			decryptConstants = new BoolOption(null, makeArgName("consts"), "Decrypt constants", true);
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
				RemoveTamperProtection = removeTamperProtection.get(),
				DecryptConstants = decryptConstants.get(),
			});
		}

		protected override IEnumerable<Option> getOptionsInternal() {
			return new List<Option>() {
				removeTamperProtection,
				decryptConstants,
			};
		}
	}

	class Deobfuscator : DeobfuscatorBase {
		Options options;
		string obfuscatorName = DeobfuscatorInfo.THE_NAME;
		bool foundCryptoObfuscatorAttribute = false;
		bool foundObfuscatedSymbols = false;
		bool foundObfuscatorUserString = false;

		MethodsDecrypter methodsDecrypter;
		ProxyCallFixer proxyCallFixer;
		ResourceDecrypter resourceDecrypter;
		ResourceResolver resourceResolver;
		AssemblyResolver assemblyResolver;
		StringDecrypter stringDecrypter;
		TamperDetection tamperDetection;
		AntiDebugger antiDebugger;
		ConstantsDecrypter constantsDecrypter;
		Int32ValueInliner int32ValueInliner;
		Int64ValueInliner int64ValueInliner;
		SingleValueInliner singleValueInliner;
		DoubleValueInliner doubleValueInliner;

		internal class Options : OptionsBase {
			public bool RemoveTamperProtection { get; set; }
			public bool DecryptConstants { get; set; }
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

		public override void init(ModuleDefinition module) {
			base.init(module);
		}

		protected override int detectInternal() {
			int val = 0;

			int sum = toInt32(methodsDecrypter.Detected) +
					toInt32(stringDecrypter.Detected) +
					toInt32(tamperDetection.Detected) +
					toInt32(proxyCallFixer.Detected) +
					toInt32(constantsDecrypter.Detected);
			if (sum > 0)
				val += 100 + 10 * (sum - 1);
			if (foundCryptoObfuscatorAttribute || foundObfuscatedSymbols || foundObfuscatorUserString)
				val += 10;

			return val;
		}

		protected override void scanForObfuscator() {
			foreach (var type in module.Types) {
				if (type.FullName == "CryptoObfuscator.ProtectedWithCryptoObfuscatorAttribute") {
					foundCryptoObfuscatorAttribute = true;
					addAttributeToBeRemoved(type, "Obfuscator attribute");
					initializeVersion(type);
				}
			}
			if (checkCryptoObfuscator())
				foundObfuscatedSymbols = true;

			methodsDecrypter = new MethodsDecrypter(module);
			methodsDecrypter.find();
			proxyCallFixer = new ProxyCallFixer(module);
			proxyCallFixer.findDelegateCreator();
			stringDecrypter = new StringDecrypter(module);
			stringDecrypter.find();
			tamperDetection = new TamperDetection(module);
			tamperDetection.find();
			constantsDecrypter = new ConstantsDecrypter(module);
			constantsDecrypter.find();
			foundObfuscatorUserString = Utils.StartsWith(module.GetUserString(0x70000001), "\u0011\"3D9B94A98B-76A8-4810-B1A0-4BE7C4F9C98D", StringComparison.Ordinal);
		}

		void initializeVersion(TypeDefinition attr) {
			var s = DotNetUtils.getCustomArgAsString(getAssemblyAttribute(attr), 0);
			if (s == null)
				return;

			var val = Regex.Match(s, @"^Protected with (Crypto Obfuscator.*)$");
			if (val.Groups.Count < 2)
				return;
			obfuscatorName = val.Groups[1].ToString();
			return;
		}

		bool checkCryptoObfuscator() {
			int matched = 0;
			foreach (var type in module.Types) {
				if (type.Namespace != "A")
					continue;
				if (Regex.IsMatch(type.Name, "^c[0-9a-f]{32}$"))
					return true;
				else if (Regex.IsMatch(type.Name, "^A[A-Z]*$")) {
					if (++matched >= 10)
						return true;
				}
			}
			return false;
		}

		public override void deobfuscateBegin() {
			base.deobfuscateBegin();

			resourceDecrypter = new ResourceDecrypter(module, DeobfuscatedFile);
			resourceResolver = new ResourceResolver(module, resourceDecrypter);
			assemblyResolver = new AssemblyResolver(module);
			resourceResolver.find();
			assemblyResolver.find();

			decryptResources();
			stringDecrypter.init(resourceDecrypter);
			if (stringDecrypter.Method != null) {
				staticStringInliner.add(stringDecrypter.Method, (method, gim, args) => {
					return stringDecrypter.decrypt((int)args[0]);
				});
				DeobfuscatedFile.stringDecryptersAdded();
			}

			methodsDecrypter.decrypt(resourceDecrypter);

			if (methodsDecrypter.Detected) {
				if (!assemblyResolver.Detected)
					assemblyResolver.find();
				if (!tamperDetection.Detected)
					tamperDetection.find();
			}
			antiDebugger = new AntiDebugger(module, DeobfuscatedFile, this);
			antiDebugger.find();

			if (options.DecryptConstants) {
				constantsDecrypter.init(resourceDecrypter);
				int32ValueInliner = new Int32ValueInliner();
				int32ValueInliner.add(constantsDecrypter.Int32Decrypter, (method, gim, args) => constantsDecrypter.decryptInt32((int)args[0]));
				int64ValueInliner = new Int64ValueInliner();
				int64ValueInliner.add(constantsDecrypter.Int64Decrypter, (method, gim, args) => constantsDecrypter.decryptInt64((int)args[0]));
				singleValueInliner = new SingleValueInliner();
				singleValueInliner.add(constantsDecrypter.SingleDecrypter, (method, gim, args) => constantsDecrypter.decryptSingle((int)args[0]));
				doubleValueInliner = new DoubleValueInliner();
				doubleValueInliner.add(constantsDecrypter.DoubleDecrypter, (method, gim, args) => constantsDecrypter.decryptDouble((int)args[0]));
				addTypeToBeRemoved(constantsDecrypter.Type, "Constants decrypter type");
				addResourceToBeRemoved(constantsDecrypter.Resource, "Encrypted constants");
			}

			addModuleCctorInitCallToBeRemoved(resourceResolver.Method);
			addModuleCctorInitCallToBeRemoved(assemblyResolver.Method);
			addCallToBeRemoved(module.EntryPoint, tamperDetection.Method);
			addModuleCctorInitCallToBeRemoved(tamperDetection.Method);
			addCallToBeRemoved(module.EntryPoint, antiDebugger.Method);
			addModuleCctorInitCallToBeRemoved(antiDebugger.Method);
			addTypeToBeRemoved(resourceResolver.Type, "Resource resolver type");
			addTypeToBeRemoved(assemblyResolver.Type, "Assembly resolver type");
			addTypeToBeRemoved(tamperDetection.Type, "Tamper detection type");
			addTypeToBeRemoved(antiDebugger.Type, "Anti-debugger type");
			addTypeToBeRemoved(methodsDecrypter.Type, "Methods decrypter type");
			addTypesToBeRemoved(methodsDecrypter.DelegateTypes, "Methods decrypter delegate type");
			addResourceToBeRemoved(methodsDecrypter.Resource, "Encrypted methods");

			proxyCallFixer.find();

			dumpEmbeddedAssemblies();
		}

		public override void deobfuscateMethodEnd(Blocks blocks) {
			proxyCallFixer.deobfuscate(blocks);
			if (options.DecryptConstants) {
				int32ValueInliner.decrypt(blocks);
				int64ValueInliner.decrypt(blocks);
				singleValueInliner.decrypt(blocks);
				doubleValueInliner.decrypt(blocks);
			}
			base.deobfuscateMethodEnd(blocks);
		}

		public override void deobfuscateEnd() {
			removeProxyDelegates(proxyCallFixer);
			if (CanRemoveStringDecrypterType) {
				addResourceToBeRemoved(stringDecrypter.Resource, "Encrypted strings");
				addTypeToBeRemoved(stringDecrypter.Type, "String decrypter type");
			}
			base.deobfuscateEnd();
		}

		void decryptResources() {
			var rsrc = resourceResolver.mergeResources();
			if (rsrc == null)
				return;
			addResourceToBeRemoved(rsrc, "Encrypted resources");
		}

		void dumpEmbeddedAssemblies() {
			foreach (var info in assemblyResolver.AssemblyInfos) {
				dumpEmbeddedFile(info.resource, info.assemblyName, ".dll", string.Format("Embedded assembly: {0}", info.assemblyName));

				if (info.symbolsResource != null)
					dumpEmbeddedFile(info.symbolsResource, info.assemblyName, ".pdb", string.Format("Embedded pdb: {0}", info.assemblyName));
			}
		}

		void dumpEmbeddedFile(EmbeddedResource resource, string assemblyName, string extension, string reason) {
			DeobfuscatedFile.createAssemblyFile(resourceDecrypter.decrypt(resource.GetResourceStream()), Utils.getAssemblySimpleName(assemblyName), extension);
			addResourceToBeRemoved(resource, reason);
		}

		public override IEnumerable<int> getStringDecrypterMethods() {
			var list = new List<int>();
			if (stringDecrypter.Method != null)
				list.Add(stringDecrypter.Method.MetadataToken.ToInt32());
			return list;
		}
	}
}
