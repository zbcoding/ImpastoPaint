export const languages = {
  en: { name: 'English', hreflang: 'en', og: 'en_US' },
  es: { name: 'Español', hreflang: 'es', og: 'es_ES' },
  fr: { name: 'Français', hreflang: 'fr', og: 'fr_FR' },
  de: { name: 'Deutsch', hreflang: 'de', og: 'de_DE' },
  ja: { name: '日本語', hreflang: 'ja', og: 'ja_JP' },
  'zh-cn': { name: '简体中文', hreflang: 'zh-CN', og: 'zh_CN' },
} as const;

export type Lang = keyof typeof languages;

export const defaultLang: Lang = 'en';

/** Path (relative to the site base) for the given language's home page. */
export function localePath(lang: Lang): string {
  return lang === defaultLang ? '/' : `/${lang}/`;
}
