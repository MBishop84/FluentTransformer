using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Newtonsoft.Json;
using System.Globalization;
using System.Text;
using System.Xml;

namespace FluentTransformer.Components.Pages
{
    public partial class Transformer
    {
        #region Private Fields
        [Inject]
        private IJSRuntime JsRuntime { get; set; }
        private string? _input, _output, _split = "\\n", _join = ", ", _brackets = "()", _parentheses = "''", _userCode;
        private bool _dynamic = true;
        private int _rows = 40;
        private readonly string entityQuery = @"
SELECT 
    TableName = tbl.table_schema + '.' + tbl.table_name, 
    ColumnName = col.column_name, 
    ColumnDataType = col.data_type,
    Nullable = col.IS_NULLABLE
FROM INFORMATION_SCHEMA.TABLES tbl
INNER JOIN INFORMATION_SCHEMA.COLUMNS col 
    ON col.table_name = tbl.table_name
    AND col.table_schema = tbl.table_schema

WHERE tbl.table_type = 'base table' and tbl.table_name = 'TableName'";

        #endregion Private Fields

        #region Constructors

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            var envHeight = await JsRuntime.InvokeAsync<int>("getDimensions");
            _rows = envHeight / 23;
        }

        #endregion Constructors

        #region Private Methods

        private void Clear() => _input = _output = string.Empty;

        private void Transform()
        {
            try
            {
                if (!string.IsNullOrEmpty(_brackets) && _brackets.Length != 2)
                {
                    throw new ArgumentException("Brackets must be 2 characters long.");
                }
                if (!string.IsNullOrEmpty(_parentheses) && _parentheses.Length != 2)
                {
                    throw new ArgumentException("Parentheses must be 2 characters long.");
                }
                var split = _split?.Replace("\\n", "\n").Replace("\\t", "\t") ?? string.Empty;
                var join = _join?.Replace("\\n", "\n").Replace("\\t", "\t") ?? string.Empty;
                
                var frontBracket = string.IsNullOrEmpty(_brackets)
                    ? string.Empty
                    : _brackets[0].ToString();
                var endBracket = string.IsNullOrEmpty(_brackets)
                    ? string.Empty
                    : _brackets[1].ToString();
                var frontParentheses = string.IsNullOrEmpty(_parentheses)
                    ? string.Empty
                    : _parentheses[0].ToString();
                var endParentheses = string.IsNullOrEmpty(_parentheses)
                    ? string.Empty
                    : _parentheses[1].ToString();

                _output = $"{frontBracket}{string.Join(join, _input?.Split(split).Select(x =>
                {
                    return _dynamic switch
                    {
                        true => int.TryParse(x, out int i)
                            ? $"{i}" : $"{frontParentheses}{x}{endParentheses}",
                        false => $"{frontParentheses}{x}{endParentheses}"
                    };
                }) ?? [])}{endBracket}";
            }
            catch (Exception ex)
            {
                _output = ex.Message;
            }
        }

        private void ClearBrackets() =>_brackets = string.Empty;
        private void ClearParentheses() => _parentheses = string.Empty;

        private async Task JavaScript()
        {
            try
            {
                var userBox = "const input = document.getElementById('input').value;\nlet output = '';\n[***]\ndocument.getElementById('output').value = output;";
                if (string.IsNullOrEmpty(_userCode))
                {
                    _userCode = @"output = input.split('\n').map(x => x.length >= 5 ? `Hello ${x}` : `GoodBye ${x}`).join(',\n');";
                }
                else
                {
                    await JsRuntime.InvokeAsync<string>("runUserScript", userBox.Replace("[***]", _userCode));
                }
            }
            catch (Exception ex)
            {
                _output = ex.Message;
            }
        }

        private void JsonToClass()
        {
            try
            {
                dynamic? jsonObject = JsonConvert.DeserializeObject($"{{{_input}}}");
                if (jsonObject != null)
                {
                    var result = new StringBuilder();
                    foreach (var i in jsonObject)
                    {
                        result.AppendFormat("///<summary>\n/// Gets/Sets the {0}.\n///</summary>\n", i.Name);
                        result.Append($"{i.Value}" switch
                        {
                            var a when int.TryParse(a, out int itemInt) =>
                                $"public int {i.Name} {{ get; set; }}\n\n",
                            var b when DateTime.TryParse(b, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime itemDate) =>
                                $"public DateTime? {i.Name} {{ get; set; }}\n\n",
                            var c when bool.TryParse(c, out bool itemBool) =>
                                $"public bool {i.Name} {{ get; set; }}\n\n",
                            _ => $"public string {i.Name} {{ get; set; }} = string.Empty;\n\n"
                        });
                    }
                    _output = result.ToString();
                }
            }
            catch (Exception ex)
            {
                _output = ex.Message;
            }
        }

        private void Entity()
        {
            if (!string.IsNullOrEmpty(_input))
            {
                try
                {
                    var lines = _input?.Split("\n") ?? [];
                    var result = new StringBuilder();

                    foreach (var line in lines)
                    {
                        var properties = line.Split("\t");
                        result.AppendFormat("///<summary>\n/// Gets/Sets the {0}.\n///</summary>\n", properties[0]);
                        switch (properties.Length)
                        {
                            case 1:
                                result.Append($"Insufficient arguments.\n\n");
                                break;
                            case 2:
                                properties[0] = properties[0].Replace("LOB", "LineOfBusiness").Replace("ID", "Id").Replace("Num", "Number").Replace("Agt", "Agent").Replace("Trans", "Transaction");
                                result.Append(properties[1] switch
                                {
                                    var a when a.Contains("int", StringComparison.OrdinalIgnoreCase) =>
                                        $"public int {properties[0]} {{ get; set; }}\n\n",
                                    var b when b.Contains("date", StringComparison.OrdinalIgnoreCase) =>
                                        $"public DateTime {properties[0]} {{ get; set; }}\n\n",
                                    var b when b.Contains("bit", StringComparison.OrdinalIgnoreCase) =>
                                        $"public bool {properties[0]} {{ get; set; }}\n\n",
                                    var b when b.Contains("unique", StringComparison.OrdinalIgnoreCase) =>
                                        $"public Guid {properties[0]} {{ get; set; }}\n\n",
                                    _ => $"public string {properties[0]} {{ get; set; }}\n\n"
                                });
                                break;
                            case 3:
                                string isNullable = properties[2].Equals("YES", StringComparison.OrdinalIgnoreCase) ? "?" : "";
                                properties[0] = properties[0].Replace("LOB", "LineOfBusiness").Replace("ID", "Id").Replace("Num", "Number").Replace("Agt", "Agent").Replace("Trans", "Transaction");
                                result.Append(properties[1] switch
                                {
                                    var a when a.Contains("int", StringComparison.OrdinalIgnoreCase) =>
                                        $"public int{isNullable} {properties[0]} {{ get; set; }}\n\n",
                                    var b when b.Contains("date", StringComparison.OrdinalIgnoreCase) =>
                                        $"public DateTime{isNullable} {properties[0]} {{ get; set; }}\n\n",
                                    var b when b.Contains("bit", StringComparison.OrdinalIgnoreCase) =>
                                        $"public bool{isNullable} {properties[0]} {{ get; set; }}\n\n",
                                    var b when b.Contains("unique", StringComparison.OrdinalIgnoreCase) =>
                                        $"public Guid{isNullable} {properties[0]} {{ get; set; }}\n\n",
                                    _ => $"public string {properties[0]} {{ get; set; }}\n\n"
                                });
                                break;
                            default:
                                break;
                        }
                    }
                    _output = result.ToString();
                }
                catch (Exception ex)
                {
                    _output = ex.Message;
                }
            }
            else
            {
                _output = entityQuery;
            }
        }

        private void JsonToXML()
        {
            try
            {
                var doc = JsonConvert.DeserializeXmlNode(
                    @"{'DefaultRoot':{" + $"{_input}}}}}"
                    );
                var sw = new StringWriter();
                var writer = new XmlTextWriter(sw)
                {
                    Formatting = System.Xml.Formatting.Indented
                };
                doc?.WriteContentTo(writer);
                _output = sw.ToString();
            }
            catch (Exception ex)
            {
                _output = ex.Message;
            }
        }

        #endregion Private Methods
    }
}