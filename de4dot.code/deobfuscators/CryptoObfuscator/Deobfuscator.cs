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

using System.Collections.Generic;
using System.Text.RegularExpressions;
using Mono.Cecil;
using de4dot.blocks;

namespace de4dot.deobfuscators.CryptoObfuscator {
	class DeobfuscatorInfo : DeobfuscatorInfoBase {
		const string DEFAULT_REGEX = @"!^[A-Z]{1,3}(?:`\d+)?$&!^c[0-9a-f]{32}(?:`\d+)?$&" + DeobfuscatorBase.DEFAULT_VALID_NAME_REGEX;
		public DeobfuscatorInfo()
			: base("co", DEFAULT_REGEX) {
		}

		internal static string ObfuscatorType {
			get { return "CryptoObfuscator"; }
		}

		public override string Type {
			get { return ObfuscatorType; }
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
		string obfuscatorName = "Crypto Obfuscator";
		bool foundCryptoObfuscatorAttribute = false;
		bool foundObfuscatedSymbols = false;

		ResourceDecrypter resourceDecrypter;
		ResourceResolver resourceResolver;
		AssemblyResolver assemblyResolver;
		StringDecrypter stringDecrypter;

		internal class Options : OptionsBase {
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
		}

		public override void init(ModuleDefinition module) {
			base.init(module);
		}

		public override int detect() {
			scanForObfuscator();

			int val = 0;

			if (foundCryptoObfuscatorAttribute)
				val += 100;
			else if (foundObfuscatedSymbols)
				val += 10;
			if (stringDecrypter.Detected)
				val += 10;

			return val;
		}

		protected override void scanForObfuscatorInternal() {
			foreach (var type in module.Types) {
				if (type.FullName == "CryptoObfuscator.ProtectedWithCryptoObfuscatorAttribute") {
					foundCryptoObfuscatorAttribute = true;
					addAttributeToBeRemoved(type, "Obfuscator attribute");
					initializeVersion(type);
				}
			}
			if (checkCryptoObfuscator())
				foundObfuscatedSymbols = true;

			stringDecrypter = new StringDecrypter(module);
			stringDecrypter.detect();
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

			resourceDecrypter = new ResourceDecrypter(module);
			resourceResolver = new ResourceResolver(module, resourceDecrypter);
			assemblyResolver = new AssemblyResolver(module);
			resourceResolver.find();
			assemblyResolver.find();

			decryptResources();
			stringDecrypter.init(resourceDecrypter);
			if (stringDecrypter.StringDecrypterMethod != null) {
				addResourceToBeRemoved(stringDecrypter.StringResource, "Encrypted strings");
				staticStringDecrypter.add(stringDecrypter.StringDecrypterMethod, (method, args) => {
					return stringDecrypter.decrypt((int)args[0]);
				});
			}

			dumpEmbeddedAssemblies();
		}

		void decryptResources() {
			var rsrc = resourceResolver.mergeResources();
			if (rsrc == null)
				return;
			addResourceToBeRemoved(rsrc, "Encrypted resources");
		}

		void dumpEmbeddedAssemblies() {
			foreach (var info in assemblyResolver.AssemblyInfos) {
				dumpEmbeddedFile(info.resource, info.assemblyName, true);

				if (info.symbolsResource != null)
					dumpEmbeddedFile(info.symbolsResource, info.assemblyName, false);
			}
		}

		void dumpEmbeddedFile(EmbeddedResource resource, string assemblyName, bool isAssembly) {
			string extension = isAssembly ? ".dll" : ".pdb";
			DeobfuscatedFile.createAssemblyFile(resourceDecrypter.decrypt(resource.GetResourceStream()), Utils.getAssemblySimpleName(assemblyName), extension);
			string reason = isAssembly ? string.Format("Embedded assembly: {0}", assemblyName) :
										 string.Format("Embedded pdb: {0}", assemblyName);
			addResourceToBeRemoved(resource, reason);
		}

		public override IEnumerable<string> getStringDecrypterMethods() {
			var list = new List<string>();
			if (stringDecrypter.StringDecrypterMethod != null)
				list.Add(stringDecrypter.StringDecrypterMethod.MetadataToken.ToInt32().ToString("X8"));
			return list;
		}
	}
}
