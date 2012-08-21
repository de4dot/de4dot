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
using System.IO;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Collections.Generic;

namespace de4dot.code.deobfuscators.Babel_NET {
	class BabelMethodreference : IGenericInstance {
		public string Name { get; set; }
		public TypeReference DeclaringType { get; set; }
		public TypeReference ReturnType { get; set; }
		public ParameterDefinition[] Parameters { get; set; }
		public TypeReference[] GenericArguments { get; set; }
		public int Flags { get; set; }

		public bool HasThis {
			get { return (Flags & 1) != 0; }
		}

		public bool IsGenericMethod {
			get { return (Flags & 2) != 0; }
		}

		bool IGenericInstance.HasGenericArguments {
			get { return IsGenericMethod; }
		}

		Collection<TypeReference> IGenericInstance.GenericArguments {
			get { return new Collection<TypeReference>(GenericArguments); }
		}

		MetadataToken IMetadataTokenProvider.MetadataToken {
			get { throw new NotImplementedException(); }
			set { throw new NotImplementedException(); }
		}
	}

	class BabelMethodDefinition : BabelMethodreference {
		ParameterDefinition thisParameter;

		public int Flags2 { get; set; }
		public short MaxStack { get; set; }
		public IList<VariableDefinition> Locals { get; set; }
		public IList<Instruction> Instructions { get; set; }
		public IList<ExceptionHandler> ExceptionHandlers { get; set; }

		public bool IsStatic {
			get { return (Flags2 & 0x10) != 0; }
		}

		public bool RequiresFatExceptionHandler {
			get { return (Flags2 & 0x20) != 0; }
		}

		public bool InitLocals {
			get { return (Flags2 & 0x40) != 0; }
		}

		public bool CacheMethod {
			get { return (Flags2 & 0x80) != 0; }
		}

		public ParameterDefinition ThisParameter {
			get {
				if (!HasThis)
					return null;
				if (thisParameter != null)
					return thisParameter;
				return thisParameter = new ParameterDefinition(DeclaringType);
			}
		}

		public void setBody(MethodBodyReader mbr) {
			Flags2 = mbr.Flags2;
			MaxStack = mbr.MaxStack;
			Locals = mbr.Locals;
			Instructions = mbr.Instructions;
			ExceptionHandlers = mbr.ExceptionHandlers;
		}

		public ParameterDefinition[] getRealParameters() {
			if (ThisParameter == null)
				return Parameters;
			var parameters = new ParameterDefinition[Parameters.Length + 1];
			parameters[0] = ThisParameter;
			Array.Copy(Parameters, 0, parameters, 1, Parameters.Length);
			return parameters;
		}
	}

	class MethodReferenceReader {
		ImageReader imageReader;
		BinaryReader reader;
		BabelMethodreference bmr;

		public MethodReferenceReader(ImageReader imageReader, BinaryReader reader)
			: this(imageReader, reader, new BabelMethodreference()) {
		}

		public MethodReferenceReader(ImageReader imageReader, BinaryReader reader, BabelMethodreference bmr) {
			this.imageReader = imageReader;
			this.reader = reader;
			this.bmr = bmr;
		}

		public BabelMethodreference read() {
			bmr.Name = imageReader.readString();
			bmr.DeclaringType = imageReader.readTypeReference();
			bmr.ReturnType = imageReader.readTypeReference();
			bmr.Parameters = readParameters();
			bmr.Flags = reader.ReadByte();
			if (bmr.IsGenericMethod)
				bmr.GenericArguments = imageReader.readTypeReferences();
			else
				bmr.GenericArguments = new TypeReference[0];
			return bmr;
		}

		ParameterDefinition[] readParameters() {
			var typeReferences = imageReader.readTypeReferences();
			var parameters = new ParameterDefinition[typeReferences.Length];
			for (int i = 0; i < parameters.Length; i++)
				parameters[i] = new ParameterDefinition(typeReferences[i]);
			return parameters;
		}
	}

	class MethodDefinitionReader {
		MethodReferenceReader methodReferenceReader;
		MethodBodyReader methodBodyReader;
		BabelMethodDefinition bmd;

		public MethodDefinitionReader(ImageReader imageReader, BinaryReader reader) {
			this.bmd = new BabelMethodDefinition();
			this.methodReferenceReader = new MethodReferenceReader(imageReader, reader, bmd);
			this.methodBodyReader = new MethodBodyReader(imageReader, reader);
		}

		public BabelMethodDefinition read() {
			methodReferenceReader.read();
			methodBodyReader.read(bmd.getRealParameters());
			bmd.setBody(methodBodyReader);
			return bmd;
		}
	}
}
