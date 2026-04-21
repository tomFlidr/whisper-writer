using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WhisperWriter.DI;

[AttributeUsage(AttributeTargets.Property)]
public class InjectAttribute: Attribute {
}