﻿using BlazorMonaco.Editor;
using Microsoft.AspNetCore.Components;
using Microsoft.FluentUI.AspNetCore.Components;
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
        [Inject]
        private IDialogService DialogService { get; set; }
        private sealed record UserSnippet(string Name, string Code);
        StandaloneCodeEditor _editor { get; set; }

        private string? _input, _output, _split = "\\n", _join = ", ", _brackets = "()", _parentheses = "''", _userCode;
        private bool _dynamic = true;
        private int _rows = 40;
        private List<UserSnippet> _snippets = [];
        private readonly string snippetFile = "Data/JsSnippets.json";
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

        protected override async Task OnInitializedAsync()
        {
            try
            {
                string json = await File.ReadAllTextAsync(snippetFile);
                if (string.IsNullOrEmpty(json))
                {
                    using StreamWriter outputFile = new(snippetFile, false);
                    await outputFile.WriteLineAsync(JsonConvert.SerializeObject(new List<UserSnippet>
                    {
                        new("//Converter", "output = input.split('\\t').map(x => `${x} = source.${x}`).join(',\\n')")
                    }));
                }
                _snippets = JsonConvert.DeserializeObject<UserSnippet[]>(json)?.ToList()!;
            }
            catch (Exception ex)
            {
                await DialogService.ShowErrorAsync(ex.Message);
            }
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            try
            {
                var envHeight = await JsRuntime.InvokeAsync<int>("getDimensions");
                _rows = envHeight / 24;

            }
            catch (Exception ex)
            {
                await DialogService.ShowErrorAsync(ex.Message);
            }
        }

        #endregion Constructors

        #region Private Methods

        private void Clear() => _input = _output = string.Empty;

        private async Task Transform()
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
                        true => int.TryParse(x, out int i) || x.Equals("null", StringComparison.OrdinalIgnoreCase)
                            ? $"{x}" : $"{frontParentheses}{x}{endParentheses}",
                        false => $"{frontParentheses}{x}{endParentheses}"
                    };
                }) ?? [])}{endBracket}";
            }
            catch (Exception ex)
            {
                await DialogService.ShowErrorAsync(ex.Message);
            }
        }

        private void ClearBrackets() => _brackets = string.Empty;
        private void ClearParentheses() => _parentheses = string.Empty;

        private async Task JavaScript()
        {
            try
            {
                _userCode = await _editor.GetValue();
                var userBox = "const input = document.getElementById('input').value;\nlet output = '';\n[***]\ndocument.getElementById('output').value = output;";
                if (string.IsNullOrEmpty(_userCode))
                {
                    _userCode = $"{_snippets.Select(x => $"{x.Name}\n{x.Code}").FirstOrDefault()}";
                }
                else
                {
                    await JsRuntime.InvokeAsync<string>("runUserScript", userBox.Replace("[***]", _userCode));
                }
            }
            catch (Exception ex)
            {
                await DialogService.ShowErrorAsync(ex.Message);
            }
        }

        private async Task SaveJs()
        {
            try
            {
                _userCode = await _editor.GetValue();
                if (string.IsNullOrEmpty(_userCode))
                    throw new ArgumentException("No code to save.");
                var userSnippet = _userCode.Split("\n");
                if (userSnippet.Length < 2)
                    throw new ArgumentException("Please include a name for your script.");

                if (!userSnippet[0].StartsWith("//"))
                    userSnippet[0] = $"//{userSnippet[0]}";

                if (userSnippet.Length > 2)
                    userSnippet[1] = string.Join("\n", userSnippet[1..]);

                if (_snippets.Exists(x => x.Name == userSnippet[0]))
                    _snippets.Remove(_snippets.First(x => x.Name == userSnippet[0]));

                _snippets.Add(new UserSnippet(userSnippet[0], userSnippet[1]));
                using StreamWriter outputFile = new(snippetFile, false);
                await outputFile.WriteLineAsync(JsonConvert.SerializeObject(_snippets));
                await DialogService.ShowSuccessAsync("Your Transform has been saved!");
            }
            catch (Exception ex)
            {
                await DialogService.ShowErrorAsync(ex.Message);
            }
        }

        private async Task DeleteJs()
        {
            try
            {
                _userCode = await _editor.GetValue();
                if (string.IsNullOrEmpty(_userCode))
                {
                    await DialogService.ShowErrorAsync("Please select a transform to delete.");
                    return;
                }
                var userSnippet = _userCode.Split("\n");

                if (!userSnippet[0].StartsWith("//"))
                    userSnippet[0] = $"//{userSnippet[0]}";

                if (!_snippets.Exists(x => x.Name == userSnippet[0]))
                {
                    await DialogService.ShowErrorAsync("No user transform found with that name!");
                    return;
                }

                var dialog = await DialogService.ShowConfirmationAsync(
                    $"Are you sure you want to permanently delete User Transform {userSnippet[0].Replace("//", "")}?");
                var result = await dialog.Result;
                if (result.Cancelled)
                    return;

                _snippets.Remove(_snippets.First(x => x.Name == userSnippet[0]));

                await using StreamWriter outputFile = new(snippetFile, false);
                await outputFile.WriteAsync(JsonConvert.SerializeObject(_snippets));
                await DialogService.ShowSuccessAsync("Your Transform has been deleted!");
                await _editor.SetValue(string.Empty);
            }
            catch (Exception ex)
            {
                await DialogService.ShowErrorAsync(ex.Message);
            }
        }

        private async Task NextJs()
        {
            try
            {
                _userCode = await _editor.GetValue();
                if (string.IsNullOrEmpty(_userCode))
                    _userCode = $"{_snippets[0].Name}\n{_snippets[0].Code}";
                else
                {
                    var index = _snippets.FindIndex(x => x.Name == _userCode.Split("\n")[0]);
                    if (index < _snippets.Count - 1)
                    {
                        _userCode = $"{_snippets[index + 1].Name}\n{_snippets[index + 1].Code}";
                    }
                    else
                        _userCode = $"{_snippets[0].Name}\n{_snippets[0].Code}";
                }
                await _editor.SetValue(_userCode);
            }
            catch (Exception ex)
            {
                await DialogService.ShowErrorAsync(ex.Message);
            }
        }

        private async Task PreviousJs()
        {
            try
            {
                _userCode = await _editor.GetValue();
                if (string.IsNullOrEmpty(_userCode))
                    _userCode = $"{_snippets[0].Name}\n{_snippets[0].Code}";
                else
                {
                    var index = _snippets.FindIndex(x => x.Name == _userCode.Split("\n")[0]);
                    if (index > 0)
                    {
                        _userCode = $"{_snippets[index - 1].Name}\n{_snippets[index - 1].Code}";
                    }
                    else
                        _userCode = $"{_snippets[^1].Name}\n{_snippets[^1].Code}";
                }
                await _editor.SetValue(_userCode);
            }
            catch (Exception ex)
            {
                await DialogService.ShowErrorAsync(ex.Message);
            }
        }

        private Task UpdateUserCode(string userSnippet) =>
            _editor.SetValue(userSnippet);

        private async Task JsonToClass()
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
                await DialogService.ShowErrorAsync(ex.Message);
            }
        }

        private async Task Entity()
        {
            if (string.IsNullOrEmpty(_input))
            {
                _output = entityQuery;
                return;
            }
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
                            properties[0] = properties[0].Replace("LOB", "LineOfBusiness").Replace("ID", "Id").Replace("Agt", "Agent").Replace("Trans", "Transaction");
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
                            properties[0] = properties[0].Replace("LOB", "LineOfBusiness").Replace("ID", "Id").Replace("Agt", "Agent").Replace("Trans", "Transaction");
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
                await DialogService.ShowErrorAsync(ex.Message);
            }
        }

        private async Task JsonToXML()
        {
            try
            {
                if (string.IsNullOrEmpty(_input))
                    throw new ArgumentException("Expected Format:\n{\"Key\":\"Value\"}\n{\"Key\":\"Value\"}");

                var brackets = _input.StartsWith('{') && _input.EndsWith('}')
                    ? [string.Empty, string.Empty]
                    : new[] { "{", "}" };

                var doc = JsonConvert.DeserializeXmlNode(
                    $"{{\"DefaultRoot\":{brackets[0]}" + $"{_input}{brackets[1]}}}");
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
                await DialogService.ShowErrorAsync(ex.Message);
            }
        }

        private async Task RowToJson()
        {
            try
            {
                if (string.IsNullOrEmpty(_input))
                {
                    throw new ArgumentException("Expected Format:\nColumn\\tColumn...\nValue\\tValue...");
                }
                var cols = _input.Split("\n")[0].Split("\t");
                var values = _input.Split("\n")[1].Split("\t");
                var result = new StringBuilder();

                for (var x = 0; x < cols.Length; x++)
                {
                    result.AppendFormat(values[x] switch
                    {
                        var a when int.TryParse(a, out int ignored) => "\t\"{0}\": {1},\n",
                        var b when b.Equals("NULL", StringComparison.OrdinalIgnoreCase) => "\t\"{0}\": \"\",\n",
                        var c when string.IsNullOrEmpty(c) => "\t\"{0}\": \"\",\n",
                        _ => "\t\"{0}\": \"{1}\",\n"
                    }, char.ToLowerInvariant(cols[x][0]) + cols[x][1..], values[x]);
                }
                result.Length -= 2;

                _output = $"{{\n{result}\n}}";
            }
            catch (IndexOutOfRangeException ex)
            {
                _output = "Invalid input format:\nExpected:\nColumn\\tColumn...\nValue\\tValue...";
            }
            catch (Exception ex)
            {
                await DialogService.ShowErrorAsync(ex.Message);
            }
        }

        private async Task Schema()
        {
            try
            {
                if (string.IsNullOrEmpty(_input))
                {
                    throw new ArgumentException("Expected Format:\nColumn\nColumn\nColumn\nColumn...");
                }
                _output = string.Join("\n\n", _input.Split('\n').Select(x =>
                {
                    return $"builder.Property(p => p.{x}).HasColumnName(\"{x}\");";
                }));
            }
            catch (Exception ex)
            {
                await DialogService.ShowErrorAsync(ex.Message);
            }
        }

        private StandaloneEditorConstructionOptions EditorConstructionOptions(StandaloneCodeEditor editor)
        {
            return new StandaloneEditorConstructionOptions
            {
                AutomaticLayout = true,
                Language = "javascript",
                Value = _userCode,
                Theme = "vs-dark",
            };
        }

        #endregion Private Methods
    }
}