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

using System;
using Mono.Cecil;

namespace de4dot.blocks {
	public abstract class Expander {
		protected bool modified = false;

		protected void checkModified(object a, object b) {
			if (!ReferenceEquals(a, b))
				modified = true;
		}
	}

	public class TypeReferenceExpander : Expander {
		TypeReference typeReference;
		GenericInstanceType git;

		public TypeReferenceExpander(TypeReference typeReference, GenericInstanceType git) {
			this.typeReference = typeReference;
			this.git = git;
		}

		// Returns same one if nothing was expanded
		public TypeReference expand() {
			var rv = expandType(typeReference);
			return modified ? rv : typeReference;
		}

		TypeReference expandType(TypeReference a) {
			if (a == null)
				return null;

			var type = MemberReferenceHelper.getMemberReferenceType(a);
			switch (type) {
			case CecilType.ArrayType:
				return expandArrayType((ArrayType)a);
			case CecilType.ByReferenceType:
				return expandByReferenceType((ByReferenceType)a);
			case CecilType.FunctionPointerType:
				return expandFunctionPointerType((FunctionPointerType)a);
			case CecilType.GenericInstanceType:
				return expandGenericInstanceType((GenericInstanceType)a);
			case CecilType.GenericParameter:
				return expandGenericParameter((GenericParameter)a);
			case CecilType.OptionalModifierType:
				return expandOptionalModifierType((OptionalModifierType)a);
			case CecilType.PinnedType:
				return expandPinnedType((PinnedType)a);
			case CecilType.PointerType:
				return expandPointerType((PointerType)a);
			case CecilType.RequiredModifierType:
				return expandRequiredModifierType((RequiredModifierType)a);
			case CecilType.SentinelType:
				return expandSentinelType((SentinelType)a);
			case CecilType.TypeDefinition:
				return expandTypeDefinition((TypeDefinition)a);
			case CecilType.TypeReference:
				return expandTypeReference((TypeReference)a);
			default:
				throw new ApplicationException(string.Format("Unknown cecil type {0}", type));
			}
		}

		ArrayType expandArrayType(ArrayType a) {
			var rv = new ArrayType(expandType(a.ElementType));
			if (!a.IsVector) {
				foreach (var dim in a.Dimensions)
					rv.Dimensions.Add(dim);
			}
			return rv;
		}

		ByReferenceType expandByReferenceType(ByReferenceType a) {
			return new ByReferenceType(expandType(a.ElementType));
		}

		FunctionPointerType expandFunctionPointerType(FunctionPointerType a) {
			var rv = new FunctionPointerType();
			rv.function = new MethodReferenceExpander(a.function, git).expand();
			checkModified(a.function, rv.function);
			return rv;
		}

		GenericInstanceType expandGenericInstanceType(GenericInstanceType a) {
			var rv = new GenericInstanceType(expandType(a.ElementType));
			foreach (var arg in a.GenericArguments)
				rv.GenericArguments.Add(expandType(arg));
			return rv;
		}

		TypeReference expandGenericParameter(GenericParameter a) {
			if (!MemberReferenceHelper.compareTypes(a.Owner as TypeReference, git.ElementType))
				return a;
			modified = true;
			return expandType(git.GenericArguments[a.Position]);
		}

		OptionalModifierType expandOptionalModifierType(OptionalModifierType a) {
			return new OptionalModifierType(expandType(a.ModifierType), expandType(a.ElementType));
		}

		PinnedType expandPinnedType(PinnedType a) {
			return new PinnedType(expandType(a.ElementType));
		}

		PointerType expandPointerType(PointerType a) {
			return new PointerType(expandType(a.ElementType));
		}

		RequiredModifierType expandRequiredModifierType(RequiredModifierType a) {
			return new RequiredModifierType(expandType(a.ModifierType), expandType(a.ElementType));
		}

		SentinelType expandSentinelType(SentinelType a) {
			return new SentinelType(expandType(a.ElementType));
		}

		TypeReference expandTypeDefinition(TypeDefinition a) {
			return expandTypeReference(a);
		}

		TypeReference expandTypeReference(TypeReference a) {
			return a;
		}
	}

	public abstract class MultiTypeExpander : Expander {
		GenericInstanceType git;

		public MultiTypeExpander(GenericInstanceType git) {
			this.git = git;
		}

		protected TypeReference expandType(TypeReference tr) {
			var type = new TypeReferenceExpander(tr, git).expand();
			checkModified(type, tr);
			return type;
		}

		protected T getResult<T>(T orig, T expanded) {
			return modified ? expanded : orig;
		}
	}

	public class MethodReferenceExpander : MultiTypeExpander {
		MethodReference methodReference;

		public MethodReferenceExpander(MethodReference methodReference, GenericInstanceType git)
			: base(git) {
			this.methodReference = methodReference;
		}

		public MethodReference expand() {
			var mr = new MethodReference(methodReference.Name, expandType(methodReference.MethodReturnType.ReturnType), methodReference.DeclaringType);
			mr.HasThis = methodReference.HasThis;
			mr.ExplicitThis = methodReference.ExplicitThis;
			mr.CallingConvention = methodReference.CallingConvention;

			if (methodReference.HasParameters) {
				foreach (var param in methodReference.Parameters) {
					var newParam = new ParameterDefinition(param.Name, param.Attributes, expandType(param.ParameterType));
					mr.Parameters.Add(newParam);
				}
			}

			if (methodReference.HasGenericParameters) {
				foreach (var param in methodReference.GenericParameters) {
					var newParam = new GenericParameter(param.Name, mr);
					mr.GenericParameters.Add(newParam);
				}
			}

			return getResult(methodReference, mr);
		}
	}

	public class FieldReferenceExpander : MultiTypeExpander {
		FieldReference fieldReference;

		public FieldReferenceExpander(FieldReference fieldReference, GenericInstanceType git)
			: base(git) {
			this.fieldReference = fieldReference;
		}

		public FieldReference expand() {
			var fr = new FieldReference(fieldReference.Name, expandType(fieldReference.FieldType));
			return getResult(fieldReference, fr);
		}
	}

	public class EventReferenceExpander : MultiTypeExpander {
		EventReference eventReference;

		public EventReferenceExpander(EventReference eventReference, GenericInstanceType git)
			: base(git) {
			this.eventReference = eventReference;
		}

		public EventReference expand() {
			var er = new EventDefinition(eventReference.Name, (EventAttributes)0, expandType(eventReference.EventType));
			return getResult(eventReference, er);
		}
	}

	public class PropertyReferenceExpander : MultiTypeExpander {
		PropertyReference propertyReference;

		public PropertyReferenceExpander(PropertyReference propertyReference, GenericInstanceType git)
			: base(git) {
			this.propertyReference = propertyReference;
		}

		public PropertyReference expand() {
			var pr = new PropertyDefinition(propertyReference.Name, (PropertyAttributes)0, expandType(propertyReference.PropertyType));
			if (propertyReference.Parameters != null) {
				foreach (var param in propertyReference.Parameters) {
					var newParam = new ParameterDefinition(param.Name, param.Attributes, expandType(param.ParameterType));
					pr.Parameters.Add(newParam);
				}
			}

			return getResult(propertyReference, pr);
		}
	}
}
