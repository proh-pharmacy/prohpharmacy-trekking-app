using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace prohpharmacy_trekking_app.Extensions
{
    public static class SwaggerDoc
    {
        // ─── Swagger Endpoint Group Names ──────────────────────────────────────────

        public static class SwaggerEndpointDefinitions
        {
            public const string General = "General";
            public const string Auth = "Auth";
            public const string Admin = "Admin";
            public const string Organisation = "Organisation";
            public const string Staff = "Staff";
            public const string Fleet = "Fleet";
            public const string Tracking = "Tracking";
            public const string Trekking = "Trekking";
            public const string Customers = "Customers";
            public const string Ledger = "Ledger";
            public const string Visits = "Visits";
            public const string Reports = "Reports";
        }

        // ─── Operation Filters ─────────────────────────────────────────────────────

        public class EndpointMetadataOperationFilter : IOperationFilter
        {
            public void Apply(OpenApiOperation operation, OperationFilterContext context)
            {
                var metadata = context.ApiDescription.ActionDescriptor?.EndpointMetadata;
                if (metadata == null) return;

                var summary = metadata
                    .OfType<IEndpointSummaryMetadata>()
                    .Select(m => m.Summary)
                    .LastOrDefault();

                var description = metadata
                    .OfType<IEndpointDescriptionMetadata>()
                    .Select(m => m.Description)
                    .LastOrDefault();

                if (!string.IsNullOrWhiteSpace(summary))
                    operation.Summary = summary;

                if (!string.IsNullOrWhiteSpace(description))
                    operation.Description = description;
            }
        }

        public class SwaggerFileOperationFilter : IOperationFilter
        {
            public void Apply(OpenApiOperation operation, OperationFilterContext context)
            {
                var fileParams = context.ApiDescription.ParameterDescriptions
                    .Where(x => x.ModelMetadata?.ElementType == typeof(IFormFile))
                    .Select(x => x.Name);

                if (fileParams.Any())
                {
                    operation.RequestBody = new OpenApiRequestBody
                    {
                        Content = new Dictionary<string, OpenApiMediaType>
                        {
                            ["multipart/form-data"] = new OpenApiMediaType
                            {
                                Schema = new OpenApiSchema
                                {
                                    Type = "object",
                                    Properties = fileParams.ToDictionary(
                                        param => param,
                                        param => new OpenApiSchema { Type = "string", Format = "binary" }),
                                    Required = fileParams.ToHashSet()
                                }
                            }
                        }
                    };
                }
            }
        }

        // ─── Schema Filters ────────────────────────────────────────────────────────

        public class EnumSchemaFilter : ISchemaFilter
        {
            public void Apply(OpenApiSchema model, SchemaFilterContext context)
            {
                if (!context.Type.IsEnum) return;

                model.Enum.Clear();
                var descriptions = new List<string>();
                foreach (var name in Enum.GetNames(context.Type))
                {
                    model.Enum.Add(new OpenApiString(name));
                    var member = context.Type.GetMember(name).FirstOrDefault();
                    var desc = member?
                        .GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false)
                        .OfType<System.ComponentModel.DescriptionAttribute>()
                        .FirstOrDefault()?.Description;
                    if (!string.IsNullOrEmpty(desc))
                        descriptions.Add($"`{name}` – {desc}");
                }

                if (descriptions.Count > 0)
                    model.Description =
                        (model.Description is { Length: > 0 } existing ? existing + "\n\n" : "") +
                        string.Join("\n\n", descriptions);
            }
        }

        // ─── Swagger Gen Config ────────────────────────────────────────────────────

        public static void OpenAuthentication(SwaggerGenOptions option)
        {
            option.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "prohpharmacy-trekking API",
                Version = "v1",
                Description = "REST API for the Prohpharmacy Trekking platform."
            });

            option.CustomSchemaIds(type =>
            {
                if (!type.IsGenericType)
                    return (type.FullName ?? type.Name).Replace("+", ".");

                var baseName = type.GetGenericTypeDefinition().Name;
                baseName = baseName[..baseName.IndexOf('`')];
                var args = string.Join("_", type.GetGenericArguments().Select(a => a.Name));
                return $"{baseName}_{args}";
            });

            option.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                In = ParameterLocation.Header,
                Description = "Enter a valid JWT bearer token.",
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                BearerFormat = "JWT",
                Scheme = "Bearer"
            });

            option.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                        {
                            Type = ReferenceType.SecurityScheme,
                            Id = "Bearer"
                        }
                    },
                    Array.Empty<string>()
                }
            });

            option.OperationFilter<EndpointMetadataOperationFilter>();
            option.OperationFilter<SwaggerFileOperationFilter>();
            option.SchemaFilter<EnumSchemaFilter>();
            option.DocInclusionPredicate((_, _) => true);
        }
    }
}
