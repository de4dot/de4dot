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

using System.Collections.Generic;
using System.Text.RegularExpressions;
using dnlib.DotNet;
using de4dot.blocks;
using de4dot.blocks.cflow;

namespace de4dot.code.deobfuscators.Babel_NET {
	public class DeobfuscatorInfo : DeobfuscatorInfoBase {
		public const string THE_NAME = "Babel .NET";
		public const string THE_TYPE = "bl";
		const string DEFAULT_REGEX = DeobfuscatorBase.DEFAULT_ASIAN_VALID_NAME_REGEX;
		BoolOption inlineMethods;
		BoolOption removeInlinedMethods;
		BoolOption decryptMethods;
		BoolOption decryptResources;
		BoolOption decryptConstants;
		BoolOption dumpEmbeddedAssemblies;

		public DeobfuscatorInfo()
			: base(DEFAULT_REGEX) {
			inlineMethods = new BoolOption(null, MakeArgName("inline"), "Inline short methods", true);
			removeInlinedMethods = new BoolOption(null, MakeArgName("remove-inlined"), "Remove inlined methods", true);
			decryptMethods = new BoolOption(null, MakeArgName("methods"), "Decrypt methods", true);
			decryptResources = new BoolOption(null, MakeArgName("rsrc"), "Decrypt resources", true);
			decryptConstants = new BoolOption(null, MakeArgName("consts"), "Decrypt constants and arrays", true);
			dumpEmbeddedAssemblies = new BoolOption(null, MakeArgName("embedded"), "Dump embedded assemblies", true);
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
				InlineMethods = inlineMethods.Get(),
				RemoveInlinedMethods = removeInlinedMethods.Get(),
				DecryptMethods = decryptMethods.Get(),
				DecryptResources = decryptResources.Get(),
				DecryptConstants = decryptConstants.Get(),
				DumpEmbeddedAssemblies = dumpEmbeddedAssemblies.Get(),
			});
		}

		protected override IEnumerable<Option> GetOptionsInternal() {
			return new List<Option>() {
				inlineMethods,
				removeInlinedMethods,
				decryptMethods,
				decryptResources,
				decryptConstants,
				dumpEmbeddedAssemblies,
			};
		}
	}

	class Deobfuscator : DeobfuscatorBase {
		Options options;
		bool foundBabelAttribute = false;
		string obfuscatorName = DeobfuscatorInfo.THE_NAME;
		bool startedDeobfuscating = false;

		ResourceResolver resourceResolver;
		AssemblyResolver assemblyResolver;
		StringDecrypter stringDecrypter;
		ConstantsDecrypter constantsDecrypter;
		Int32ValueInliner int32ValueInliner;
		Int64ValueInliner int64ValueInliner;
		SingleValueInliner singleValueInliner;
		DoubleValueInliner doubleValueInliner;
		ProxyCallFixer proxyCallFixer;
		MethodsDecrypter methodsDecrypter;

		internal class Options : OptionsBase {
			public bool InlineMethods { get; set; }
			public bool RemoveInlinedMethods { get; set; }
			public bool DecryptMethods { get; set; }
			public bool DecryptResources { get; set; }
			public bool DecryptConstants { get; set; }
			public bool DumpEmbeddedAssemblies { get; set; }
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

		protected override bool CanInlineMethods {
			get { return startedDeobfuscating ? options.InlineMethods : true; }
		}

		public override IEnumerable<IBlocksDeobfuscator> BlocksDeobfuscators {
			get {
				var list = new List<IBlocksDeobfuscator>();
				if (CanInlineMethods)
					list.Add(new BabelMethodCallInliner());
				return list;
			}
		}

		public Deobfuscator(Options options)
			: base(options) {
			this.options = options;
		}

		public override void Initialize(ModuleDefMD module) {
			base.Initialize(module);
		}

		protected override int DetectInternal() {
			int val = 0;

			int sum = ToInt32(resourceResolver.Detected) +
					ToInt32(assemblyResolver.Detected) +
					ToInt32(stringDecrypter.Detected) +
					ToInt32(constantsDecrypter.Detected) +
					ToInt32(proxyCallFixer.Detected) +
					ToInt32(methodsDecrypter.Detected) +
					ToInt32(HasMetadataStream("Babel"));
			if (sum > 0)
				val += 100 + 10 * (sum - 1);
			if (foundBabelAttribute)
				val += 10;

			return val;
		}

		protected override void ScanForObfuscator() {
			FindBabelAttribute();
			var resourceDecrypterCreator = new ResourceDecrypterCreator(module, DeobfuscatedFile);
			resourceResolver = new ResourceResolver(module, resourceDecrypterCreator.Create(), DeobfuscatedFile);
			resourceResolver.Find();
			assemblyResolver = new AssemblyResolver(module, resourceDecrypterCreator.Create());
			assemblyResolver.Find();
			stringDecrypter = new StringDecrypter(module, resourceDecrypterCreator.Create());
			stringDecrypter.Find(DeobfuscatedFile);
			constantsDecrypter = new ConstantsDecrypter(module, resourceDecrypterCreator.Create(), initializedDataCreator);
			constantsDecrypter.Find();
			proxyCallFixer = new ProxyCallFixer(module);
			proxyCallFixer.FindDelegateCreator();
			methodsDecrypter = new MethodsDecrypter(module, resourceDecrypterCreator.Create(), DeobfuscatedFile.DeobfuscatorContext);
			methodsDecrypter.Find();
		}

		void FindBabelAttribute() {
			foreach (var type in module.Types) {
				if (type.FullName == "BabelAttribute" || type.FullName == "BabelObfuscatorAttribute") {
					foundBabelAttribute = true;
					CheckVersion(type);
					AddAttributeToBeRemoved(type, "Obfuscator attribute");
					return;
				}
			}
		}

		void CheckVersion(TypeDef attr) {
			var versionField = attr.FindField("Version");
			if (versionField != null && versionField.IsLiteral && versionField.Constant != null && versionField.Constant.Value is string) {
				var val = Regex.Match((string)versionField.Constant.Value, @"^(\d+\.\d+\.\d+\.\d+)$");
				if (val.Groups.Count < 2)
					return;
				obfuscatorName = string.Format("{0} {1}", DeobfuscatorInfo.THE_NAME, val.Groups[1].ToString());
				return;
			}
		}

		public override void DeobfuscateBegin() {
			base.DeobfuscateBegin();

			if (options.DecryptResources) {
				AddCctorInitCallToBeRemoved(resourceResolver.InitMethod);
				AddTypeToBeRemoved(resourceResolver.Type, "Resource resolver type");
			}

			DecryptResources();
			stringDecrypter.Initialize();

			if (Operations.DecryptStrings != OpDecryptString.None) {
				if (stringDecrypter.Resource != null)
					Logger.v("Adding string decrypter. Resource: {0}", Utils.ToCsharpString(stringDecrypter.Resource.Name));
				staticStringInliner.Add(stringDecrypter.DecryptMethod, (method, gim, args) => {
					return stringDecrypter.Decrypt(args);
				});
				DeobfuscatedFile.StringDecryptersAdded();
			}

			if (options.DumpEmbeddedAssemblies) {
				assemblyResolver.Initialize(DeobfuscatedFile, this);

				// Need to dump the assemblies before decrypting methods in case there's a reference
				// in the encrypted code to one of these assemblies.
				DumpEmbeddedAssemblies();
			}

			if (options.DecryptMethods) {
				methodsDecrypter.Initialize(DeobfuscatedFile, this);
				methodsDecrypter.decrypt();
			}

			if (options.DecryptConstants) {
				constantsDecrypter.Initialize(DeobfuscatedFile, this);

				AddTypeToBeRemoved(constantsDecrypter.Type, "Constants decrypter type");
				AddResourceToBeRemoved(constantsDecrypter.Resource, "Encrypted constants");
				int32ValueInliner = new Int32ValueInliner();
				int32ValueInliner.Add(constantsDecrypter.Int32Decrypter, (method, gim, args) => constantsDecrypter.DecryptInt32((int)args[0]));
				int64ValueInliner = new Int64ValueInliner();
				int64ValueInliner.Add(constantsDecrypter.Int64Decrypter, (method, gim, args) => constantsDecrypter.DecryptInt64((int)args[0]));
				singleValueInliner = new SingleValueInliner();
				singleValueInliner.Add(constantsDecrypter.SingleDecrypter, (method, gim, args) => constantsDecrypter.DecryptSingle((int)args[0]));
				doubleValueInliner = new DoubleValueInliner();
				doubleValueInliner.Add(constantsDecrypter.DoubleDecrypter, (method, gim, args) => constantsDecrypter.DecryptDouble((int)args[0]));
			}

			proxyCallFixer.Find();
			startedDeobfuscating = true;
		}

		void DumpEmbeddedAssemblies() {
			if (!options.DumpEmbeddedAssemblies)
				return;
			foreach (var info in assemblyResolver.EmbeddedAssemblyInfos)
				DeobfuscatedFile.CreateAssemblyFile(info.data, Utils.GetAssemblySimpleName(info.fullname), info.extension);
			AddTypeToBeRemoved(assemblyResolver.Type, "Assembly resolver type");
			AddCctorInitCallToBeRemoved(assemblyResolver.InitMethod);
			AddResourceToBeRemoved(assemblyResolver.EncryptedResource, "Embedded encrypted assemblies");
		}

		void DecryptResources() {
			if (!options.DecryptResources)
				return;
			var rsrc = resourceResolver.MergeResources();
			if (rsrc == null)
				return;
			AddResourceToBeRemoved(rsrc, "Encrypted resources");
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
			RemoveInlinedMethods();
			if (CanRemoveStringDecrypterType) {
				AddResourceToBeRemoved(stringDecrypter.Resource, "Encrypted strings");
				AddTypeToBeRemoved(stringDecrypter.Type, "String decrypter type");
			}

			RemoveProxyDelegates(proxyCallFixer);
			base.DeobfuscateEnd();
		}

		void RemoveInlinedMethods() {
			if (!options.InlineMethods || !options.RemoveInlinedMethods)
				return;
			RemoveInlinedMethods(BabelMethodCallInliner.Find(module, staticStringInliner.Methods));
		}

		public override IEnumerable<int> GetStringDecrypterMethods() {
			var list = new List<int>();
			if (stringDecrypter.DecryptMethod != null)
				list.Add(stringDecrypter.DecryptMethod.MDToken.ToInt32());
			return list;
		}
	}
}
