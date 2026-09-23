// highlight.js core with the languages a .NET / web blog uses. Registering languages one by one keeps the
// bundle a fraction of the size of highlight.js/lib/common. Add a language here when posts need it; code
// blocks in other languages still render, just without colors.

import hljs from 'highlight.js/lib/core';
import bash from 'highlight.js/lib/languages/bash';
import csharp from 'highlight.js/lib/languages/csharp';
import css from 'highlight.js/lib/languages/css';
import diff from 'highlight.js/lib/languages/diff';
import dockerfile from 'highlight.js/lib/languages/dockerfile';
import javascript from 'highlight.js/lib/languages/javascript';
import json from 'highlight.js/lib/languages/json';
import markdown from 'highlight.js/lib/languages/markdown';
import plaintext from 'highlight.js/lib/languages/plaintext';
import powershell from 'highlight.js/lib/languages/powershell';
import python from 'highlight.js/lib/languages/python';
import scss from 'highlight.js/lib/languages/scss';
import shell from 'highlight.js/lib/languages/shell';
import sql from 'highlight.js/lib/languages/sql';
import typescript from 'highlight.js/lib/languages/typescript';
import xml from 'highlight.js/lib/languages/xml';
import yaml from 'highlight.js/lib/languages/yaml';

const languages = {
    bash, csharp, css, diff, dockerfile, javascript, json, markdown, plaintext, powershell, python, scss,
    shell, sql, typescript, xml, yaml
};

for (const [name, language] of Object.entries(languages)) {
    hljs.registerLanguage(name, language);
}

// Razor markup shares the XML/HTML grammar closely enough for a blog.
hljs.registerAliases(['razor', 'cshtml'], { languageName: 'xml' });

// Markdown fences are authored by a trusted admin, and Markdig escapes their content.
hljs.configure({ ignoreUnescapedHTML: true });

export default hljs;
