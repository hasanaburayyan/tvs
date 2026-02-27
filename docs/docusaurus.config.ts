import {themes as prismThemes} from 'prism-react-renderer';
import type {Config} from '@docusaurus/types';
import type * as Preset from '@docusaurus/preset-classic';

const config: Config = {
  title: 'Trench Valkyries: Sacrifice',
  tagline: 'Alternate-reality WW1 tactical RPG',
  favicon: 'img/favicon.ico',

  future: {
    v4: true,
  },

  url: 'https://trench-valkyries.example.com',
  baseUrl: '/',

  onBrokenLinks: 'throw',

  markdown: {
    mermaid: true,
  },

  themes: ['@docusaurus/theme-mermaid'],

  i18n: {
    defaultLocale: 'en',
    locales: ['en'],
  },

  presets: [
    [
      'classic',
      {
        docs: {
          sidebarPath: './sidebars.ts',
        },
        blog: false,
        theme: {
          customCss: './src/css/custom.css',
        },
      } satisfies Preset.Options,
    ],
  ],

  themeConfig: {
    colorMode: {
      defaultMode: 'dark',
      respectPrefersColorScheme: true,
    },
    navbar: {
      title: 'Trench Valkyries: Sacrifice',
      items: [
        {
          type: 'docSidebar',
          sidebarId: 'mainSidebar',
          position: 'left',
          label: 'Docs',
        },
      ],
    },
    footer: {
      style: 'dark',
      links: [
        {
          title: 'Docs',
          items: [
            {
              label: 'Project Overview',
              to: '/docs/intro',
            },
            {
              label: 'Roadmap',
              to: '/docs/roadmap/overview',
            },
          ],
        },
        {
          title: 'Resources',
          items: [
            {
              label: 'SpacetimeDB Docs',
              href: 'https://spacetimedb.com/docs',
            },
            {
              label: 'SpacetimeDB Unity Guide',
              href: 'https://spacetimedb.com/docs/unity',
            },
          ],
        },
      ],
      copyright: `Copyright © ${new Date().getFullYear()} Trench Valkyries: Sacrifice`,
    },
    prism: {
      theme: prismThemes.github,
      darkTheme: prismThemes.dracula,
      additionalLanguages: ['csharp', 'bash', 'json'],
    },
  } satisfies Preset.ThemeConfig,
};

export default config;
