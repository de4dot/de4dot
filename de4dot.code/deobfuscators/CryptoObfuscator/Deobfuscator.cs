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
using System.Text.RegularExpressions;
using dnlib.DotNet;
using de4dot.blocks;
using de4dot.blocks.cflow;

namespace de4dot.code.deobfuscators.CryptoObfuscator {
	public class DeobfuscatorInfo : DeobfuscatorInfoBase {
		public const string THE_NAME = "Crypto Obfuscator";
		public const string THE_TYPE = "co";
		const string DEFAULT_REGEX = @"!^(get_|set_|add_|remove_)?[A-Z]{1,3}(?:`\d+)?$&!^(get_|set_|add_|remove_)?c[0-9a-f]{32}(?:`\d+)?$&" + DeobfuscatorBase.DEFAULT_ASIAN_VALID_NAME_REGEX;
		BoolOption removeTamperProtection;
		BoolOption decryptConstants;
		BoolOption inlineMethods;
		BoolOption fixLdnull;

		public DeobfuscatorInfo()
			: base(DEFAULT_REGEX) {
			removeTamperProtection = new BoolOption(null, MakeArgName("tamper"), "Remove tamper protection code", true);
			decryptConstants = new BoolOption(null, MakeArgName("consts"), "Decrypt constants", true);
			inlineMethods = new BoolOption(null, MakeArgName("inline"), "Inline short methods", true);
			fixLdnull = new BoolOption(null, MakeArgName("ldnull"), "Restore ldnull instructions", true);
		}

		public override string Name => THE_NAME;
		public override string Type => THE_TYPE;

		public override IDeobfuscator CreateDeobfuscator() =>
			new Deobfuscator(new Deobfuscator.Options {
				ValidNameRegex = validNameRegex.Get(),
				RemoveTamperProtection = removeTamperProtection.Get(),
				DecryptConstants = decryptConstants.Get(),
				InlineMethods = inlineMethods.Get(),
				FixLdnull = fixLdnull.Get(),
			});

		protected override IEnumerable<Option> GetOptionsInternal() =>
			new List<Option>() {
				removeTamperProtection,
				decryptConstants,
				inlineMethods,
				fixLdnull,
			};
	}

	class Deobfuscator : DeobfuscatorBase {
		Options options;
		string obfuscatorName = DeobfuscatorInfo.THE_NAME;
		bool foundCryptoObfuscatorAttribute = false;
		bool foundObfuscatedSymbols = false;
		bool foundObfuscatorUserString = false;
		bool startedDeobfuscating = false;

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
		InlinedMethodTypes inlinedMethodTypes;

		internal class Options : OptionsBase {
			public bool RemoveTamperProtection { get; set; }
			public bool DecryptConstants { get; set; }
			public bool InlineMethods { get; set; }
			public bool FixLdnull { get; set; }
		}

		public override string Type => DeobfuscatorInfo.THE_TYPE;
		public override string TypeLong => DeobfuscatorInfo.THE_NAME;
		public override string Name => obfuscatorName;
		protected override bool CanInlineMethods => startedDeobfuscating ? options.InlineMethods : true;

		public override IEnumerable<IBlocksDeobfuscator> BlocksDeobfuscators {
			get {
				var list = new List<IBlocksDeobfuscator>();
				if (CanInlineMethods)
					list.Add(new CoMethodCallInliner(inlinedMethodTypes));
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

			int sum = ToInt32(methodsDecrypter.Detected) +
					ToInt32(stringDecrypter.Detected) +
					ToInt32(tamperDetection.Detected) +
					ToInt32(proxyCallFixer.Detected) +
					ToInt32(constantsDecrypter.Detected);
			if (sum > 0)
				val += 100 + 10 * (sum - 1);
			if (foundCryptoObfuscatorAttribute || foundObfuscatedSymbols || foundObfuscatorUserString)
				val += 10;

			return val;
		}

		protected override void ScanForObfuscator() {
			foreach (var type in module.Types) {
				if (type.FullName == "CryptoObfuscator.ProtectedWithCryptoObfuscatorAttribute") {
					foundCryptoObfuscatorAttribute = true;
					AddAttributeToBeRemoved(type, "Obfuscator attribute");
					InitializeVersion(type);
				}
			}
			if (CheckCryptoObfuscator())
				foundObfuscatedSymbols = true;

			inlinedMethodTypes = new InlinedMethodTypes();
			methodsDecrypter = new MethodsDecrypter(module);
			methodsDecrypter.Find();
			proxyCallFixer = new ProxyCallFixer(module);
			proxyCallFixer.FindDelegateCreator();
			stringDecrypter = new StringDecrypter(module);
			stringDecrypter.Find();
			tamperDetection = new TamperDetection(module);
			tamperDetection.Find();
			constantsDecrypter = new ConstantsDecrypter(module, initializedDataCreator);
			constantsDecrypter.Find();
			foundObfuscatorUserString = Utils.StartsWith(module.ReadUserString(0x70000001), "\u0011\"3D9B94A98B-76A8-4810-B1A0-4BE7C4F9C98D", StringComparison.Ordinal);
		}

		void InitializeVersion(TypeDef attr) {
			var s = DotNetUtils.GetCustomArgAsString(GetAssemblyAttribute(attr), 0);
			if (s == null)
				return;

			var val = Regex.Match(s, @"^Protected with (Crypto Obfuscator.*)$");
			if (val.Groups.Count < 2)
				return;
			obfuscatorName = val.Groups[1].ToString();
			return;
		}

		bool CheckCryptoObfuscator() {
			int matched = 0;
			foreach (var type in module.Types) {
				if (type.Namespace != "A")
					continue;
				if (Regex.IsMatch(type.Name.String, "^c[0-9a-f]{32}$"))
					return true;
				else if (Regex.IsMatch(type.Name.String, "^A[A-Z]*$")) {
					if (++matched >= 10)
						return true;
				}
			}
			return false;
		}

		public override void DeobfuscateBegin() {
			base.DeobfuscateBegin();

			resourceDecrypter = new ResourceDecrypter(module, DeobfuscatedFile);
			resourceResolver = new ResourceResolver(module, resourceDecrypter);
			assemblyResolver = new AssemblyResolver(module);
			resourceResolver.Find();
			assemblyResolver.Find(DeobfuscatedFile);

			DecryptResources();
			stringDecrypter.Initialize(resourceDecrypter);
			if (stringDecrypter.Method != null) {
				staticStringInliner.Add(stringDecrypter.Method, (method, gim, args) => {
					return stringDecrypter.Decrypt((int)args[0]);
				});
				DeobfuscatedFile.StringDecryptersAdded();
			}

			methodsDecrypter.Decrypt(resourceDecrypter, DeobfuscatedFile);

			if (methodsDecrypter.Detected) {
				if (!assemblyResolver.Detected)
					assemblyResolver.Find(DeobfuscatedFile);
				if (!tamperDetection.Detected)
					tamperDetection.Find();
			}
			antiDebugger = new AntiDebugger(module, DeobfuscatedFile, this);
			antiDebugger.Find();

			if (options.DecryptConstants) {
				constantsDecrypter.Initialize(resourceDecrypter);
				int32ValueInliner = new Int32ValueInliner();
				int32ValueInliner.Add(constantsDecrypter.Int32Decrypter, (method, gim, args) => constantsDecrypter.DecryptInt32((int)args[0]));
				int64ValueInliner = new Int64ValueInliner();
				int64ValueInliner.Add(constantsDecrypter.Int64Decrypter, (method, gim, args) => constantsDecrypter.DecryptInt64((int)args[0]));
				singleValueInliner = new SingleValueInliner();
				singleValueInliner.Add(constantsDecrypter.SingleDecrypter, (method, gim, args) => constantsDecrypter.DecryptSingle((int)args[0]));
				doubleValueInliner = new DoubleValueInliner();
				doubleValueInliner.Add(constantsDecrypter.DoubleDecrypter, (method, gim, args) => constantsDecrypter.DecryptDouble((int)args[0]));
				AddTypeToBeRemoved(constantsDecrypter.Type, "Constants decrypter type");
				AddResourceToBeRemoved(constantsDecrypter.Resource, "Encrypted constants");
			}

			AddModuleCctorInitCallToBeRemoved(resourceResolver.Method);
			AddModuleCctorInitCallToBeRemoved(assemblyResolver.Method);
			AddCallToBeRemoved(module.EntryPoint, tamperDetection.Method);
			AddModuleCctorInitCallToBeRemoved(tamperDetection.Method);
			AddCallToBeRemoved(module.EntryPoint, antiDebugger.Method);
			AddModuleCctorInitCallToBeRemoved(antiDebugger.Method);
			AddTypeToBeRemoved(resourceResolver.Type, "Resource resolver type");
			AddTypeToBeRemoved(assemblyResolver.Type, "Assembly resolver type");
			AddTypeToBeRemoved(tamperDetection.Type, "Tamper detection type");
			AddTypeToBeRemoved(antiDebugger.Type, "Anti-debugger type");
			AddTypeToBeRemoved(methodsDecrypter.Type, "Methods decrypter type");
			AddTypesToBeRemoved(methodsDecrypter.DelegateTypes, "Methods decrypter delegate type");
			AddResourceToBeRemoved(methodsDecrypter.Resource, "Encrypted methods");

			proxyCallFixer.Find();

			DumpEmbeddedAssemblies();

			startedDeobfuscating = true;
		}

		public override void DeobfuscateMethodEnd(Blocks blocks) {
			proxyCallFixer.Deobfuscate(blocks);
			if (options.DecryptConstants) {
				int32ValueInliner.Decrypt(blocks);
				int64ValueInliner.Decrypt(blocks);
				singleValueInliner.Decrypt(blocks);
				doubleValueInliner.Decrypt(blocks);
				constantsDecrypter.Deobfuscate(blocks);
			}
			base.DeobfuscateMethodEnd(blocks);
		}

		public override void DeobfuscateEnd() {
			if (options.FixLdnull)
				new LdnullFixer(module, inlinedMethodTypes).Restore();
			RemoveProxyDelegates(proxyCallFixer);
			if (CanRemoveStringDecrypterType) {
				AddResourceToBeRemoved(stringDecrypter.Resource, "Encrypted strings");
				AddTypeToBeRemoved(stringDecrypter.Type, "String decrypter type");
			}
			AddTypesToBeRemoved(inlinedMethodTypes.Types, "Inlined methods type");
			base.DeobfuscateEnd();
		}

		void DecryptResources() {
			var rsrc = resourceResolver.MergeResources();
			if (rsrc == null)
				return;
			AddResourceToBeRemoved(rsrc, "Encrypted resources");
		}

		void DumpEmbeddedAssemblies() {
			foreach (var info in assemblyResolver.AssemblyInfos) {
				DumpEmbeddedFile(info.resource, info.assemblyName, ".dll", $"Embedded assembly: {info.assemblyName}");

				if (info.symbolsResource != null)
					DumpEmbeddedFile(info.symbolsResource, info.assemblyName, ".pdb", $"Embedded pdb: {info.assemblyName}");
			}
		}

		void DumpEmbeddedFile(EmbeddedResource resource, string assemblyName, string extension, string reason) {
			DeobfuscatedFile.CreateAssemblyFile(resourceDecrypter.Decrypt(resource.CreateReader().AsStream()), Utils.GetAssemblySimpleName(assemblyName), extension);
			AddResourceToBeRemoved(resource, reason);
		}

		public override IEnumerable<int> GetStringDecrypterMethods() {
			var list = new List<int>();
			if (stringDecrypter.Method != null)
				list.Add(stringDecrypter.Method.MDToken.ToInt32());
			return list;
		}
	}
}
