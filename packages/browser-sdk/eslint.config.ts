import js from '@eslint/js'
import eslintConfigPrettier from 'eslint-config-prettier/flat'
import pluginOxlint from 'eslint-plugin-oxlint'
import tseslint from 'typescript-eslint'

export default tseslint.config(
  { ignores: ['dist/**'] },
  js.configs.recommended,
  tseslint.configs.recommended,
  ...pluginOxlint.buildFromOxlintConfigFile('.oxlintrc.json'),
  eslintConfigPrettier,
)
