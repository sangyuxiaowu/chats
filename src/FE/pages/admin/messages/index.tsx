import React, { useEffect, useState } from 'react';

import useDebounce from '@/hooks/useDebounce';
import useTranslation from '@/hooks/useTranslation';

import { formatDateTime } from '@/utils/date';

import { AdminChatsDto } from '@/types/adminApis';
import { PageResult, Paging } from '@/types/page';

import PaginationContainer from '../../../components/Pagiation/Pagiation';
import ChatIcon from '@/components/ChatIcon/ChatIcon';
import Tips from '@/components/Tips/Tips';
import { Badge } from '@/components/ui/badge';
import { Card } from '@/components/ui/card';
import { Input } from '@/components/ui/input';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';

import { getMessages } from '@/apis/adminApis';
import { cn } from '@/lib/utils';

export default function Messages() {
  const { t } = useTranslation();
  const [loading, setLoading] = useState(true);
  const [pagination, setPagination] = useState<Paging>({
    page: 1,
    pageSize: 12,
  });
  const [messages, setMessages] = useState<PageResult<AdminChatsDto[]>>({
    count: 0,
    rows: [],
  });
  const [query, setQuery] = useState('');

  const updateQueryWithDebounce = useDebounce((query: string) => {
    init(query);
  }, 1000);

  const init = (query: string = '') => {
    getMessages({ ...pagination, query }).then((data) => {
      setMessages(data);
      setLoading(false);
    });
  };

  useEffect(() => {
    init(query);
  }, [pagination]);

  return (
    <>
      <div className="flex flex-warp gap-4 mb-4">
        <Input
          className="max-w-[238px] w-full"
          placeholder={t('Search...')!}
          value={query}
          onChange={(e) => {
            setQuery(e.target.value);
            updateQueryWithDebounce(e.target.value);
          }}
        />
      </div>
      <Card>
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>{t('Title')}</TableHead>
              <TableHead>{t('Model')}</TableHead>
              <TableHead>{t('User Name')}</TableHead>
              <TableHead>{t('Created Time')}</TableHead>
              <TableHead>{t('Status')}</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody isLoading={loading} isEmpty={messages.count === 0}>
            {messages?.rows.map((item) => (
              <TableRow key={item.id}>
                <TableCell
                  onClick={() => {
                    window.open('/message/' + item.id, '_blank');
                  }}
                  className="truncate cursor-pointer"
                >
                  {item.title}
                </TableCell>
                <TableCell>
                  <div className="flex overflow-hidden">
                    {item.spans.map((x, index) => (
                      <div
                        key={'message-chat-icon-wrapper-' + x.modelId}
                        className={cn(
                          "flex-shrink-0 relative",
                          index > 0 && "-ml-2.5"
                        )}
                        style={{ zIndex: item.spans.length - index }}
                      >
                        <Tips
                          trigger={
                            <div>
                              <ChatIcon
                                className="cursor-pointer"
                                providerId={x.modelProviderId}
                              />
                            </div>
                          }
                          side="bottom"
                          content={x.modelName}
                        />
                      </div>
                    ))}
                  </div>
                </TableCell>
                <TableCell>{item.username}</TableCell>
                <TableCell>{formatDateTime(item.createdAt)}</TableCell>
                <TableCell>
                  {item.isDeleted && (
                    <Badge variant="destructive">{t('Deleted')}</Badge>
                  )}
                  {item.isShared && (
                    <Badge className=" bg-green-600">{t('Shared')}</Badge>
                  )}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
        {messages.count !== 0 && (
          <PaginationContainer
            page={pagination.page}
            pageSize={pagination.pageSize}
            currentCount={messages.rows.length}
            totalCount={messages.count}
            onPagingChange={(page, pageSize) => {
              setPagination({ page, pageSize });
            }}
          />
        )}
      </Card>
    </>
  );
}
