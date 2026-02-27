import type {SidebarsConfig} from '@docusaurus/plugin-content-docs';

const sidebars: SidebarsConfig = {
  mainSidebar: [
    'intro',
    {
      type: 'category',
      label: 'Roadmap',
      link: {
        type: 'doc',
        id: 'roadmap/overview',
      },
      items: [
        'roadmap/phase-0-foundation',
        'roadmap/phase-1-battlefield',
        'roadmap/phase-2-squad-command',
        'roadmap/phase-3-terrain',
        'roadmap/phase-4-magic',
        'roadmap/phase-5-vehicles',
        'roadmap/phase-6-objectives',
      ],
    },
  ],
};

export default sidebars;
