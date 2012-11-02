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

#if PORT
using System;
using Mono.Cecil;

namespace de4dot.blocks {
	public abstract class TypeReferenceUpdaterBase {
		public TypeReference update(TypeReference a) {
			if (a == null)
				return null;

			throw new NotImplementedException();
		}

		protected virtual ArrayType updateArrayType(ArrayType a) {
			var rv = new ArrayType(update(a.ElementType));
			if (!a.IsVector) {
				foreach (var dim in a.Dimensions)
					rv.Dimensions.Add(dim);
			}
			return rv;
		}

		protected virtual ByReferenceType updateByReferenceType(ByReferenceType a) {
			return new ByReferenceType(update(a.ElementType));
		}

		protected virtual FunctionPointerType updateFunctionPointerType(FunctionPointerType a) {
			var rv = new FunctionPointerType();
			rv.function = a.function;
			return rv;
		}

		protected virtual GenericInstanceType updateGenericInstanceType(GenericInstanceType a) {
			var rv = new GenericInstanceType(update(a.ElementType));
			foreach (var arg in a.GenericArguments)
				rv.GenericArguments.Add(update(arg));
			return rv;
		}

		protected virtual TypeReference updateGenericParameter(GenericParameter a) {
			return a;
		}

		protected virtual OptionalModifierType updateOptionalModifierType(OptionalModifierType a) {
			return new OptionalModifierType(update(a.ModifierType), update(a.ElementType));
		}

		protected virtual PinnedType updatePinnedType(PinnedType a) {
			return new PinnedType(update(a.ElementType));
		}

		protected virtual PointerType updatePointerType(PointerType a) {
			return new PointerType(update(a.ElementType));
		}

		protected virtual RequiredModifierType updateRequiredModifierType(RequiredModifierType a) {
			return new RequiredModifierType(update(a.ModifierType), update(a.ElementType));
		}

		protected virtual SentinelType updateSentinelType(SentinelType a) {
			return new SentinelType(update(a.ElementType));
		}

		protected virtual TypeReference updateTypeDefinition(TypeDefinition a) {
			return updateTypeReference(a);
		}

		protected virtual TypeReference updateTypeReference(TypeReference a) {
			return a;
		}
	}
}
#endif
