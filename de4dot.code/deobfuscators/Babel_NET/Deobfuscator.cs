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

using System.Collections.Generic;
using System.Text.RegularExpressions;
using Mono.Cecil;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.Babel_NET {
	public class DeobfuscatorInfo : DeobfuscatorInfoBase {
		public const string THE_NAME = "Babel .NET";
		public const string THE_TYPE = "bl";
		BoolOption decryptMethods;
		BoolOption decryptResources;
		BoolOption decryptConstants;
		BoolOption dumpEmbeddedAssemblies;

		public DeobfuscatorInfo()
			: base() {
			decryptMethods = new BoolOption(null, makeArgName("methods"), "Decrypt methods", true);
			decryptResources = new BoolOption(null, makeArgName("rsrc"), "Decrypt resources", true);
			decryptConstants = new BoolOption(null, makeArgName("consts"), "Decrypt constants and arrays", true);
			dumpEmbeddedAssemblies = new BoolOption(null, makeArgName("embedded"), "Dump embedded assemblies", true);
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
				DecryptMethods = decryptMethods.get(),
				DecryptResources = decryptResources.get(),
				DecryptConstants = decryptConstants.get(),
				DumpEmbeddedAssemblies = dumpEmbeddedAssemblies.get(),
			});
		}

		protected override IEnumerable<Option> getOptionsInternal() {
			return new List<Option>() {
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

		ResourceResolver resourceResolver;
		AssemblyResolver assemblyResolver;
		StringDecrypter stringDecrypter;
		ConstantsDecrypter constantsDecrypter;
		Int32ValueInliner int32ValueInliner;
		Int64ValueInliner int64ValueInliner;
		SingleValueInliner singleValueInliner;
		DoubleValueInliner doubleValueInliner;
		ProxyDelegateFinder proxyDelegateFinder;
		MethodsDecrypter methodsDecrypter;

		internal class Options : OptionsBase {
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

		public Deobfuscator(Options options)
			: base(options) {
			this.options = options;
		}

		public override void init(ModuleDefinition module) {
			base.init(module);
		}

		protected override int detectInternal() {
			int val = 0;

			int sum = toInt32(foundBabelAttribute) +
					toInt32(resourceResolver.Detected) +
					toInt32(assemblyResolver.Detected) +
					toInt32(stringDecrypter.Detected) +
					toInt32(constantsDecrypter.Detected) +
					toInt32(proxyDelegateFinder.Detected) +
					toInt32(methodsDecrypter.Detected) +
					toInt32(hasMetadataStream("Babel"));
			if (sum > 0)
				val += 100 + 10 * (sum - 1);

			return val;
		}

		protected override void scanForObfuscator() {
			findBabelAttribute();
			resourceResolver = new ResourceResolver(module);
			resourceResolver.find();
			assemblyResolver = new AssemblyResolver(module);
			assemblyResolver.find();
			stringDecrypter = new StringDecrypter(module);
			stringDecrypter.find();
			constantsDecrypter = new ConstantsDecrypter(module, initializedDataCreator);
			constantsDecrypter.find();
			proxyDelegateFinder = new ProxyDelegateFinder(module);
			proxyDelegateFinder.findDelegateCreator();
			methodsDecrypter = new MethodsDecrypter(module);
			methodsDecrypter.find();
		}

		void findBabelAttribute() {
			foreach (var type in module.Types) {
				if (type.FullName == "BabelAttribute" || type.FullName == "BabelObfuscatorAttribute") {
					foundBabelAttribute = true;
					checkVersion(type);
					addAttributeToBeRemoved(type, "Obfuscator attribute");
					return;
				}
			}
		}

		void checkVersion(TypeDefinition attr) {
			var versionField = DotNetUtils.getField(attr, "Version");
			if (versionField != null && versionField.IsLiteral && versionField.Constant != null && versionField.Constant is string) {
				var val = Regex.Match((string)versionField.Constant, @"^(\d+\.\d+\.\d+\.\d+)$");
				if (val.Groups.Count < 2)
					return;
				obfuscatorName = string.Format("{0} {1}", DeobfuscatorInfo.THE_NAME, val.Groups[1].ToString());
				return;
			}
		}

		public override void deobfuscateBegin() {
			base.deobfuscateBegin();

			if (options.DecryptResources) {
				addCctorInitCallToBeRemoved(resourceResolver.InitMethod);
				addTypeToBeRemoved(resourceResolver.Type, "Resource resolver type");
			}

			decryptResources();
			stringDecrypter.initialize();

			if (Operations.DecryptStrings != OpDecryptString.None) {
				addResourceToBeRemoved(stringDecrypter.Resource, "Encrypted strings");
				addTypeToBeRemoved(stringDecrypter.Type, "String decrypter type");

				if (stringDecrypter.Resource != null)
					Log.v("Adding string decrypter. Resource: {0}", Utils.toCsharpString(stringDecrypter.Resource.Name));
				staticStringDecrypter.add(stringDecrypter.DecryptMethod, (method, args) => {
					return stringDecrypter.decrypt(args);
				});
				DeobfuscatedFile.stringDecryptersAdded();
			}

			if (options.DumpEmbeddedAssemblies) {
				assemblyResolver.initialize(DeobfuscatedFile, this);

				// Need to dump the assemblies before decrypting methods in case there's a reference
				// in the encrypted code to one of these assemblies.
				dumpEmbeddedAssemblies();
			}

			if (options.DecryptMethods) {
				methodsDecrypter.initialize(DeobfuscatedFile, this);
				methodsDecrypter.decrypt();
			}

			if (options.DecryptConstants) {
				constantsDecrypter.initialize(DeobfuscatedFile, this);

				addTypeToBeRemoved(constantsDecrypter.Type, "Constants decrypter type");
				addResourceToBeRemoved(constantsDecrypter.Resource, "Encrypted constants");
				int32ValueInliner = new Int32ValueInliner();
				int32ValueInliner.add(constantsDecrypter.Int32Decrypter, (method, args) => constantsDecrypter.decryptInt32((int)args[0]));
				int64ValueInliner = new Int64ValueInliner();
				int64ValueInliner.add(constantsDecrypter.Int64Decrypter, (method, args) => constantsDecrypter.decryptInt64((int)args[0]));
				singleValueInliner = new SingleValueInliner();
				singleValueInliner.add(constantsDecrypter.SingleDecrypter, (method, args) => constantsDecrypter.decryptSingle((int)args[0]));
				doubleValueInliner = new DoubleValueInliner();
				doubleValueInliner.add(constantsDecrypter.DoubleDecrypter, (method, args) => constantsDecrypter.decryptDouble((int)args[0]));
			}

			proxyDelegateFinder.find();
		}

		void dumpEmbeddedAssemblies() {
			if (!options.DumpEmbeddedAssemblies)
				return;
			foreach (var info in assemblyResolver.EmbeddedAssemblyInfos)
				DeobfuscatedFile.createAssemblyFile(info.data, Utils.getAssemblySimpleName(info.fullname), info.extension);
			addTypeToBeRemoved(assemblyResolver.Type, "Assembly resolver type");
			addCctorInitCallToBeRemoved(assemblyResolver.InitMethod);
			addResourceToBeRemoved(assemblyResolver.EncryptedResource, "Embedded encrypted assemblies");
		}

		void decryptResources() {
			if (!options.DecryptResources)
				return;
			var rsrc = resourceResolver.mergeResources();
			if (rsrc == null)
				return;
			addResourceToBeRemoved(rsrc, "Encrypted resources");
		}

		public override void deobfuscateMethodEnd(Blocks blocks) {
			proxyDelegateFinder.deobfuscate(blocks);
			if (options.DecryptConstants) {
				int32ValueInliner.decrypt(blocks);
				int64ValueInliner.decrypt(blocks);
				singleValueInliner.decrypt(blocks);
				doubleValueInliner.decrypt(blocks);
				constantsDecrypter.deobfuscate(blocks);
			}
			base.deobfuscateMethodEnd(blocks);
		}

		public override void deobfuscateEnd() {
			removeProxyDelegates(proxyDelegateFinder);
			methodsDecrypter.Dispose();
			base.deobfuscateEnd();
		}

		public override IEnumerable<string> getStringDecrypterMethods() {
			var list = new List<string>();
			if (stringDecrypter.DecryptMethod != null)
				list.Add(stringDecrypter.DecryptMethod.MetadataToken.ToInt32().ToString("X8"));
			return list;
		}
	}
}
