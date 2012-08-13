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
using Mono.Cecil;
using Mono.MyStuff;
using de4dot.blocks;
using de4dot.blocks.cflow;
using de4dot.code.renamer;
using de4dot.PE;

namespace de4dot.code.deobfuscators {
	public interface IDeobfuscatorOptions {
		bool RenameResourcesInCode { get; }
	}

	public enum DecrypterType {
		Default,
		None,
		Static,
		Delegate,
		Emulate,
	}

	[Flags]
	public enum StringFeatures {
		AllowNoDecryption = 1,
		AllowStaticDecryption = 2,
		AllowDynamicDecryption = 4,
		AllowAll = AllowNoDecryption | AllowStaticDecryption | AllowDynamicDecryption,
	}

	[Flags]
	public enum RenamingOptions {
		RemoveNamespaceIfOneType = 1,
		RenameResourceKeys = 2,
	}

	public interface IDeobfuscator : INameChecker {
		string Type { get; }
		string TypeLong { get; }
		string Name { get; }
		IDeobfuscatorOptions TheOptions { get; }
		IOperations Operations { get; set; }
		StringFeatures StringFeatures { get; }
		RenamingOptions RenamingOptions { get; }
		DecrypterType DefaultDecrypterType { get; }
		IEnumerable<IBlocksDeobfuscator> BlocksDeobfuscators { get; }

		// This is non-null only in detect() and deobfuscateBegin().
		IDeobfuscatedFile DeobfuscatedFile { get; set; }

		// Returns null or the unpacked .NET PE file
		byte[] unpackNativeFile(PeImage peImage);

		void init(ModuleDefinition module);

		// Returns 0 if it's not detected, or > 0 if detected (higher value => more likely true).
		// This method is always called.
		int detect();

		// If the obfuscator has encrypted parts of the file, then this method should return the
		// decrypted file. true is returned if args have been initialized, false otherwise.
		bool getDecryptedModule(int count, ref byte[] newFileData, ref DumpedMethods dumpedMethods);

		// This is only called if getDecryptedModule() != null, and after the module has been
		// reloaded. Should return a new IDeobfuscator with the same options and the new module.
		IDeobfuscator moduleReloaded(ModuleDefinition module);

		// Called before all other deobfuscation methods
		void deobfuscateBegin();

		// Called before the code is deobfuscated
		void deobfuscateMethodBegin(Blocks blocks);

		// Return true if we should deobfuscate control flow again
		bool deobfuscateOther(Blocks blocks);

		// Called after deobfuscateMethodBegin() but before deobfuscateMethodEnd()
		void deobfuscateStrings(Blocks blocks);

		// Called after the code has been deobfuscated
		void deobfuscateMethodEnd(Blocks blocks);

		// Called after all deobfuscation methods
		void deobfuscateEnd();

		// Returns all string decrypter method tokens
		IEnumerable<int> getStringDecrypterMethods();
	}
}
